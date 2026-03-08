using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using PatchHound.Core.Interfaces;

namespace PatchHound.Infrastructure.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PatchHoundDbContext>
{
    public PatchHoundDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PatchHoundDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Database=patchhound_design;Username=postgres;Password=postgres"
        );

        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext>(new DesignTimeTenantContext());
        var serviceProvider = services.BuildServiceProvider();

        return new PatchHoundDbContext(optionsBuilder.Options, serviceProvider);
    }

    private class DesignTimeTenantContext : ITenantContext
    {
        public Guid? CurrentTenantId => null;
        public IReadOnlyList<Guid> AccessibleTenantIds => Array.Empty<Guid>();
        public Guid CurrentUserId => Guid.Empty;
        public bool IsSystemContext => false;
        public bool HasAccessToTenant(Guid tenantId) => false;
        public IReadOnlyList<string> GetRolesForTenant(Guid tenantId) => Array.Empty<string>();
    }
}
