using Microsoft.EntityFrameworkCore;
using Vigil.Core.Interfaces;
using Vigil.Infrastructure.Data;

namespace Vigil.Api.Auth;

public class TenantContext : ITenantContext
{
    private readonly Lazy<IReadOnlyList<Guid>> _accessibleTenantIds;
    private readonly Lazy<Guid> _currentUserId;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TenantContext(IHttpContextAccessor httpContextAccessor, VigilDbContext dbContext)
    {
        _httpContextAccessor = httpContextAccessor;

        // Lazy-load to avoid querying DB on every request if not needed
        _currentUserId = new Lazy<Guid>(() =>
        {
            var oid =
                _httpContextAccessor
                    .HttpContext?.User?.FindFirst(
                        "http://schemas.microsoft.com/identity/claims/objectidentifier"
                    )
                    ?.Value ?? _httpContextAccessor.HttpContext?.User?.FindFirst("oid")?.Value;

            if (oid is null || !Guid.TryParse(oid, out _))
                return Guid.Empty;

            // Look up internal user ID from EntraObjectId
            var user = dbContext
                .Users.AsNoTracking()
                .IgnoreQueryFilters()
                .FirstOrDefault(u => u.EntraObjectId == oid);

            return user?.Id ?? Guid.Empty;
        });

        _accessibleTenantIds = new Lazy<IReadOnlyList<Guid>>(() =>
        {
            var userId = CurrentUserId;
            if (userId == Guid.Empty)
                return Array.Empty<Guid>();

            return dbContext
                .UserTenantRoles.AsNoTracking()
                .IgnoreQueryFilters()
                .Where(utr => utr.UserId == userId)
                .Select(utr => utr.TenantId)
                .Distinct()
                .ToList();
        });
    }

    public Guid? CurrentTenantId
    {
        get
        {
            // If user has access to exactly one tenant, that's the current one
            // Otherwise, the tenant is determined by query parameter or header
            var tenants = AccessibleTenantIds;
            return tenants.Count == 1 ? tenants[0] : null;
        }
    }

    public IReadOnlyList<Guid> AccessibleTenantIds => _accessibleTenantIds.Value;
    public Guid CurrentUserId => _currentUserId.Value;
}
