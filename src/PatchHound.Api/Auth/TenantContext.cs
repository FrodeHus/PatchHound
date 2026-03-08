using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

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

    internal async Task InitializeAsync(HttpContext httpContext, PatchHoundDbContext dbContext)
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

            var user = await dbContext
                .Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.EntraObjectId == oid);

            if (user is null)
            {
                try
                {
                    user = User.Create(email.Trim(), displayName.Trim(), oid);
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
                )
                {
                    user.UpdateProfile(normalizedEmail, normalizedDisplayName);
                    await dbContext.SaveChangesAsync();
                }
            }

            _currentUserId = user.Id;

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

        if (!string.IsNullOrWhiteSpace(tokenTenantId) && _normalizedClaimRoles.Count > 0)
        {
            var internalTenantIds = await dbContext
                .Tenants.AsNoTracking()
                .IgnoreQueryFilters()
                .Where(tenant => tenant.EntraTenantId == tokenTenantId)
                .Select(tenant => tenant.Id)
                .ToListAsync();

            foreach (var tenantId in internalTenantIds)
            {
                if (!_rolesByTenantId.TryGetValue(tenantId, out var existingRoles))
                {
                    _rolesByTenantId[tenantId] = _normalizedClaimRoles.ToList();
                    continue;
                }

                _rolesByTenantId[tenantId] = existingRoles
                    .Concat(_normalizedClaimRoles)
                    .Distinct()
                    .ToList();
            }
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
