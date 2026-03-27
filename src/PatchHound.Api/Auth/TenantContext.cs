using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Auth;

public class TenantContext : ITenantContext
{
    private Guid _currentUserId;
    private IReadOnlyList<Guid> _accessibleTenantIds = Array.Empty<Guid>();
    private Dictionary<Guid, List<RoleName>> _rolesByTenantId = new();
    private IReadOnlyList<RoleName> _normalizedClaimRoles = Array.Empty<RoleName>();
    private Guid? _currentTenantId;
    private bool _initialized;

    public Guid? CurrentTenantId => _currentTenantId;
    public IReadOnlyList<Guid> AccessibleTenantIds => _accessibleTenantIds;
    public Guid CurrentUserId => _currentUserId;
    public bool IsSystemContext => false;

    public bool HasAccessToTenant(Guid tenantId) => _accessibleTenantIds.Contains(tenantId);

    public IReadOnlyList<string> GetRolesForTenant(Guid tenantId)
    {
        if (_rolesByTenantId.TryGetValue(tenantId, out var roles))
            return roles.Select(r => r.ToString()).ToList();

        return Array.Empty<string>();
    }

    internal IReadOnlyList<RoleName> GetRoleNamesForTenant(Guid tenantId)
    {
        if (_rolesByTenantId.TryGetValue(tenantId, out var roles))
            return roles;

        return Array.Empty<RoleName>();
    }

    internal IReadOnlyList<RoleName> GetAllRoleNames()
    {
        return _rolesByTenantId.Values.SelectMany(r => r).Distinct().ToList();
    }

    internal async Task InitializeAsync(
        HttpContext httpContext,
        PatchHoundDbContext dbContext,
        TeamMembershipRuleService teamMembershipRuleService
    )
    {
        if (_initialized)
            return;

        _initialized = true;

        var oid =
            httpContext
                .User?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")
                ?.Value ?? httpContext.User?.FindFirst("oid")?.Value;
        var tokenTenantId = httpContext.User?.FindFirst("tid")?.Value;
        _normalizedClaimRoles = EntraRoleNormalizer.Normalize(
            httpContext.User is null ? [] : RoleClaimReader.ReadClaims(httpContext.User)
        );

        if (oid is not null && Guid.TryParse(oid, out _))
        {
            var email =
                httpContext.User?.FindFirstValue("preferred_username")
                ?? httpContext.User?.FindFirstValue(ClaimTypes.Upn)
                ?? httpContext.User?.FindFirstValue(ClaimTypes.Email)
                ?? httpContext.User?.FindFirstValue("email")
                ?? $"{oid}@local.patchhound";
            var displayName =
                httpContext.User?.FindFirstValue("name")
                ?? httpContext.User?.FindFirstValue(ClaimTypes.Name)
                ?? email;
            var company =
                httpContext.User?.FindFirstValue("companyName")
                ?? httpContext.User?.FindFirstValue("company")
                ?? httpContext.User?.FindFirstValue("organization");

            var user = await dbContext
                .Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.EntraObjectId == oid);

            if (user is null)
            {
                try
                {
                    user = User.Create(email.Trim(), displayName.Trim(), oid, company);
                    await dbContext.Users.AddAsync(user);
                    await dbContext.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    // Concurrent request already created this user — reload
                    dbContext.ChangeTracker.Clear();
                    user = await dbContext
                        .Users.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(u => u.EntraObjectId == oid);
                    if (user is null)
                        return;
                }
            }
            else
            {
                var normalizedEmail = email.Trim();
                var normalizedDisplayName = displayName.Trim();
                if (
                    !string.Equals(user.Email, normalizedEmail, StringComparison.Ordinal)
                    || !string.Equals(
                        user.DisplayName,
                        normalizedDisplayName,
                        StringComparison.Ordinal
                    )
                    || !string.Equals(user.Company, string.IsNullOrWhiteSpace(company) ? null : company.Trim(), StringComparison.Ordinal)
                )
                {
                    user.UpdateProfile(normalizedEmail, normalizedDisplayName, company);
                    await dbContext.SaveChangesAsync();
                }
            }

            if (!user.IsEnabled)
            {
                _currentUserId = user.Id;
                _accessibleTenantIds = Array.Empty<Guid>();
                _rolesByTenantId = [];
                return;
            }

            _currentUserId = user.Id;
        }

        if (!string.IsNullOrWhiteSpace(tokenTenantId))
        {
            var internalTenantIds = await dbContext
                .Tenants.AsNoTracking()
                .IgnoreQueryFilters()
                .Where(tenant => tenant.EntraTenantId == tokenTenantId)
                .Select(tenant => tenant.Id)
                .ToListAsync();

            if (_currentUserId != Guid.Empty && internalTenantIds.Count > 0)
            {
                // Stakeholder is always granted; merge with any Entra claim roles
                var rolesToSync = _normalizedClaimRoles.ToList();
                if (!rolesToSync.Contains(RoleName.Stakeholder))
                    rolesToSync.Add(RoleName.Stakeholder);

                var existingTokenRoles = await dbContext
                    .UserTenantRoles.IgnoreQueryFilters()
                    .Where(role =>
                        role.UserId == _currentUserId
                        && internalTenantIds.Contains(role.TenantId)
                    )
                    .Select(role => new { role.TenantId, role.Role })
                    .ToListAsync();

                var missingRoles = internalTenantIds
                    .SelectMany(tenantId =>
                        rolesToSync.Select(roleName => new { tenantId, roleName })
                    )
                    .Where(candidate => existingTokenRoles.All(existing =>
                        existing.TenantId != candidate.tenantId || existing.Role != candidate.roleName))
                    .ToList();

                if (missingRoles.Count > 0)
                {
                    await dbContext.UserTenantRoles.AddRangeAsync(
                        missingRoles.Select(candidate =>
                            UserTenantRole.Create(_currentUserId, candidate.tenantId, candidate.roleName)),
                        CancellationToken.None
                    );
                    await dbContext.SaveChangesAsync();
                }
            }

            if (internalTenantIds.Count > 0)
            {
                foreach (var tenantId in internalTenantIds)
                {
                    await teamMembershipRuleService.ApplyForUserAsync(tenantId, _currentUserId, CancellationToken.None);
                }
            }

            var userTenantRoles = _currentUserId == Guid.Empty
                ? []
                : await dbContext
                    .UserTenantRoles.AsNoTracking()
                    .IgnoreQueryFilters()
                    .Where(utr => utr.UserId == _currentUserId)
                    .Select(utr => new { utr.TenantId, utr.Role })
                    .ToListAsync();

            _rolesByTenantId = userTenantRoles
                .GroupBy(r => r.TenantId)
                .ToDictionary(g => g.Key, g => g.Select(r => r.Role).Distinct().ToList());

            // Ensure Stakeholder + Entra claim roles are in the in-memory map
            var rolesToMerge = _normalizedClaimRoles.ToList();
            if (!rolesToMerge.Contains(RoleName.Stakeholder))
                rolesToMerge.Add(RoleName.Stakeholder);

            foreach (var tenantId in internalTenantIds)
            {
                if (!_rolesByTenantId.TryGetValue(tenantId, out var existingRoles))
                {
                    _rolesByTenantId[tenantId] = rolesToMerge;
                    continue;
                }

                _rolesByTenantId[tenantId] = existingRoles
                    .Concat(rolesToMerge)
                    .Distinct()
                    .ToList();
            }
        }
        else if (_currentUserId != Guid.Empty)
        {
            var userTenantRoles = await dbContext
                .UserTenantRoles.AsNoTracking()
                .IgnoreQueryFilters()
                .Where(utr => utr.UserId == _currentUserId)
                .Select(utr => new { utr.TenantId, utr.Role })
                .ToListAsync();

            _rolesByTenantId = userTenantRoles
                .GroupBy(r => r.TenantId)
                .ToDictionary(g => g.Key, g => g.Select(r => r.Role).Distinct().ToList());
        }

        _accessibleTenantIds = _rolesByTenantId.Keys.ToList();

        // Resolve current tenant: prefer explicit header, fall back to single-tenant
        if (
            httpContext.Request.Headers.TryGetValue("X-Tenant-Id", out var tenantHeader)
            && Guid.TryParse(tenantHeader.FirstOrDefault(), out var requestedTenantId)
            && _accessibleTenantIds.Contains(requestedTenantId)
        )
        {
            _currentTenantId = requestedTenantId;
        }
        else if (_accessibleTenantIds.Count == 1)
        {
            _currentTenantId = _accessibleTenantIds[0];
        }
    }
}
