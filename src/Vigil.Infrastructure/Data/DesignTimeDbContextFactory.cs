using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Vigil.Core.Interfaces;

namespace Vigil.Infrastructure.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<VigilDbContext>
{
    public VigilDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<VigilDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=vigil_design;Username=postgres;Password=postgres");

        return new VigilDbContext(optionsBuilder.Options, new DesignTimeTenantContext());
    }

    private class DesignTimeTenantContext : ITenantContext
    {
        public Guid? CurrentTenantId => null;
        public IReadOnlyList<Guid> AccessibleTenantIds => Array.Empty<Guid>();
        public Guid CurrentUserId => Guid.Empty;
    }
}
