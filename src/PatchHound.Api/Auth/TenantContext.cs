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
    public const string BlockedTenantAccessItemsKey = "__blocked_tenant_access_attempts";

    private Guid _currentUserId;
    private IReadOnlyList<Guid> _accessibleTenantIds = Array.Empty<Guid>();
    private Dictionary<Guid, List<RoleName>> _rolesByTenantId = new();
    private IReadOnlyList<RoleName> _normalizedClaimRoles = Array.Empty<RoleName>();
    private UserAccessScope _currentAccessScope = UserAccessScope.Internal;
    private HttpContext? _httpContext;
    private Guid? _currentTenantId;
    private bool _initialized;

    public Guid? CurrentTenantId => _currentTenantId;
    public IReadOnlyList<Guid> AccessibleTenantIds => _accessibleTenantIds;
    public Guid CurrentUserId => _currentUserId;
    public bool IsSystemContext => false;
    public bool IsInternalUser => _currentAccessScope == UserAccessScope.Internal;
    public UserAccessScope CurrentAccessScope => _currentAccessScope;

    public bool HasAccessToTenant(Guid tenantId)
    {
        var hasAccess = _accessibleTenantIds.Contains(tenantId);
        if (!hasAccess)
        {
            TrackBlockedAccess(tenantId, "Requested tenant is outside the current user's allowed tenant scope.");
        }

        return hasAccess;
    }

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
        _httpContext = httpContext;

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
                _currentAccessScope = user.AccessScope;
                _accessibleTenantIds = Array.Empty<Guid>();
                _rolesByTenantId = [];
                return;
            }

            _currentUserId = user.Id;
            _currentAccessScope = user.AccessScope;
        }

        if (_currentUserId != Guid.Empty)
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

            if (_currentAccessScope == UserAccessScope.Internal)
            {
                var allTenantIds = await dbContext
                    .Tenants.AsNoTracking()
                    .IgnoreQueryFilters()
                    .OrderBy(tenant => tenant.Name)
                    .Select(tenant => tenant.Id)
                    .ToListAsync();

                var rolesToMerge = _normalizedClaimRoles.ToList();
                if (!rolesToMerge.Contains(RoleName.Stakeholder))
                {
                    rolesToMerge.Add(RoleName.Stakeholder);
                }

                foreach (var tenantId in allTenantIds)
                {
                    if (!_rolesByTenantId.TryGetValue(tenantId, out var existingRoles))
                    {
                        _rolesByTenantId[tenantId] = rolesToMerge.ToList();
                    }
                    else
                    {
                        _rolesByTenantId[tenantId] = existingRoles
                            .Concat(rolesToMerge)
                            .Distinct()
                            .ToList();
                    }

                    await teamMembershipRuleService.ApplyForUserAsync(
                        tenantId,
                        _currentUserId,
                        CancellationToken.None
                    );
                }
            }
            else if (!string.IsNullOrWhiteSpace(tokenTenantId))
            {
                var matchingTenantIds = await dbContext
                    .Tenants.AsNoTracking()
                    .IgnoreQueryFilters()
                    .Where(tenant => tenant.EntraTenantId == tokenTenantId)
                    .Select(tenant => tenant.Id)
                    .ToListAsync();

                foreach (var tenantId in matchingTenantIds.Where(_rolesByTenantId.ContainsKey))
                {
                    await teamMembershipRuleService.ApplyForUserAsync(
                        tenantId,
                        _currentUserId,
                        CancellationToken.None
                    );
                }
            }
        }

        _accessibleTenantIds = _rolesByTenantId.Keys.ToList();

        // Resolve current tenant: prefer explicit header, fall back to single-tenant
        if (
            httpContext.Request.Headers.TryGetValue("X-Tenant-Id", out var tenantHeader)
        )
        {
            var headerValue = tenantHeader.FirstOrDefault();
            if (Guid.TryParse(headerValue, out var requestedTenantId))
            {
                if (_accessibleTenantIds.Contains(requestedTenantId))
                {
                    _currentTenantId = requestedTenantId;
                }
                else
                {
                    TrackBlockedAccess(requestedTenantId, "Tenant header requested a tenant outside the allowed tenant scope.");
                }
            }
        }
        else if (_accessibleTenantIds.Count == 1)
        {
            _currentTenantId = _accessibleTenantIds[0];
        }
    }

    private void TrackBlockedAccess(Guid attemptedTenantId, string reason)
    {
        var httpContext = _httpContext;
        if (httpContext is null)
        {
            return;
        }

        if (!httpContext.Items.TryGetValue(BlockedTenantAccessItemsKey, out var existing)
            || existing is not List<BlockedTenantAccessAttempt> attempts)
        {
            attempts = [];
            httpContext.Items[BlockedTenantAccessItemsKey] = attempts;
        }

        if (attempts.Any(item => item.AttemptedTenantId == attemptedTenantId && item.Reason == reason))
        {
            return;
        }

        attempts.Add(new BlockedTenantAccessAttempt(
            attemptedTenantId,
            reason,
            httpContext.Request.Path,
            httpContext.Request.Method
        ));
    }
}
