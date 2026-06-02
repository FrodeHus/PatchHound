using FluentAssertions;
using PatchHound.Core.Models.OperationalContext;
using PatchHound.Core.Services.OperationalContext;
using PatchHound.Infrastructure.Services.OperationalContext;
using PatchHound.Tests.Infrastructure;
using Xunit;

namespace PatchHound.Tests.Infrastructure.Services.OperationalContext;

[Collection(PostgresCollection.Name)]
public class AiOperationalContextServiceTests(PostgresFixture fixture)
{
    private static AiOperationalContextOptions Options() => new()
    {
        MaxTokens = 100000,
        ProviderIsExternal = false,
        IncludeDeviceNames = true,
    };

    private static AiOperationalContextService CreateService(
        PatchHound.Infrastructure.Data.PatchHoundDbContext db)
    {
        var estimator = new HeuristicPromptTokenEstimator();
        return new AiOperationalContextService(
            db,
            estimator,
            new OperationalContextRedactor(),
            new OperationalContextTruncator(estimator));
    }

    [Fact]
    public async Task RemediationCase_pack_includes_criticality_distribution_for_tenant()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateDbContext();
        var seed = await OperationalContextSeed.SeedRemediationCaseAsync(db);

        var service = CreateService(db);

        var result = await service.BuildForRemediationCaseAsync(
            seed.TenantId, seed.RemediationCaseId, Options(), CancellationToken.None);

        result.Pack.ContextKind.Should().Be("RemediationCase");
        result.Pack.Scope.AffectedDeviceCount.Should().Be(2);
        result.Pack.Scope.CriticalityDistribution.Should().ContainKey("Critical");
        result.PackJson.Should().Contain("\"contextKind\":\"RemediationCase\"");
    }

    [Fact]
    public async Task RemediationCase_pack_excludes_other_tenant_devices_for_same_software()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateDbContext();
        var seed = await OperationalContextSeed.SeedRemediationCaseAsync(db);
        await OperationalContextSeed.SeedOtherTenantExposureForSameSoftwareAsync(db, seed);

        var service = CreateService(db);

        var result = await service.BuildForRemediationCaseAsync(
            seed.TenantId, seed.RemediationCaseId, Options(), CancellationToken.None);

        // Only the 2 devices from seed.TenantId — never the other tenant's device.
        result.Pack.Scope.AffectedDeviceCount.Should().Be(2);
    }

}
