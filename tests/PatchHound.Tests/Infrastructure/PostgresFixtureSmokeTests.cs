using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PatchHound.Infrastructure.Data;
using Xunit;

namespace PatchHound.Tests.Infrastructure;

[Collection(PostgresCollection.Name)]
public class PostgresFixtureSmokeTests
{
    private readonly PostgresFixture _fx;

    public PostgresFixtureSmokeTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task Migrations_apply_and_DeviceVulnerabilityExposures_table_exists()
    {
        await using var db = _fx.CreateDbContext();
        await db.Database.MigrateAsync();

        // Verify migrations actually applied the expected table.
        await using var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'DeviceVulnerabilityExposures'";
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        count.Should().BeGreaterThan(0);
    }
}
