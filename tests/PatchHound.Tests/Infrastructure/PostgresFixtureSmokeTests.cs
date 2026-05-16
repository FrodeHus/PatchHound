using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
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
        // PostgresFixture.InitializeAsync already ran migrations once per fixture lifetime,
        // so this test only needs to verify the schema is present.
        await using var db = _fx.CreateDbContext();

        var entityType = db.Model.FindEntityType(typeof(DeviceVulnerabilityExposure))!;
        var tableName = entityType.GetTableName()!;
        var schema = entityType.GetSchema() ?? "public";

        await using var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = @schema AND table_name = @name";
        var schemaParam = cmd.CreateParameter();
        schemaParam.ParameterName = "schema";
        schemaParam.Value = schema;
        cmd.Parameters.Add(schemaParam);
        var nameParam = cmd.CreateParameter();
        nameParam.ParameterName = "name";
        nameParam.Value = tableName;
        cmd.Parameters.Add(nameParam);

        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        count.Should().BeGreaterThan(0);
    }
}
