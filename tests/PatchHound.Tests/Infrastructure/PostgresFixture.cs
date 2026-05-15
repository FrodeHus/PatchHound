using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Tests.TestData;
using Testcontainers.PostgreSql;
using Xunit;

namespace PatchHound.Tests.Infrastructure;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    public PatchHoundDbContext CreateDbContext(ITenantContext? tenantContext = null)
    {
        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(tenantContext ?? CreateDefaultTenantContext()));
    }

    public async Task ResetAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("""
            DO $$
            DECLARE r record;
            BEGIN
                FOR r IN SELECT tablename FROM pg_tables
                         WHERE schemaname = 'public' AND tablename <> '__EFMigrationsHistory'
                LOOP
                    EXECUTE 'TRUNCATE TABLE "' || r.tablename || '" CASCADE';
                END LOOP;
            END $$;
            """);
    }

    private static ITenantContext CreateDefaultTenantContext()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.CurrentTenantId.Returns(tenantId);
        tenantContext.AccessibleTenantIds.Returns([tenantId]);
        tenantContext.IsSystemContext.Returns(false);
        return tenantContext;
    }
}
