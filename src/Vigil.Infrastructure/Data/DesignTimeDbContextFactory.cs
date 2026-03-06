using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using Vigil.Core.Interfaces;

namespace Vigil.Infrastructure.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<VigilDbContext>
{
    public VigilDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<VigilDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Database=vigil_design;Username=postgres;Password=postgres"
        );

        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext>(new DesignTimeTenantContext());
        var serviceProvider = services.BuildServiceProvider();

        return new VigilDbContext(optionsBuilder.Options, serviceProvider);
    }

    private class DesignTimeTenantContext : ITenantContext
    {
        public Guid? CurrentTenantId => null;
        public IReadOnlyList<Guid> AccessibleTenantIds => Array.Empty<Guid>();
        public Guid CurrentUserId => Guid.Empty;
    }
}
