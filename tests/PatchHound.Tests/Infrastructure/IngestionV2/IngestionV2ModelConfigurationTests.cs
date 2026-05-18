using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities.Ingestion;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Tests.Infrastructure.IngestionV2;

public sealed class IngestionV2ModelConfigurationTests
{
    [Fact]
    public void Observation_model_defines_expected_unique_indexes()
    {
        using var db = TestDbContextFactory.CreateSystemContext();

        IndexNamesFor<RawDeviceObservation>(db).Should()
            .Contain("UX_RawDeviceObservations_Run_Source_ExternalId");
        IndexNamesFor<RawSoftwareObservation>(db).Should()
            .Contain("UX_RawSoftwareObservations_Run_Source_ExternalId");
        IndexNamesFor<RawInstallationObservation>(db).Should()
            .Contain("UX_RawInstallationObservations_Run_Device_Software");
        IndexNamesFor<RawVulnerabilityObservation>(db).Should()
            .Contain("UX_RawVulnerabilityObservations_Run_Source_ExternalId");
        IndexNamesFor<RawExposureObservation>(db).Should()
            .Contain("UX_RawExposureObservations_Run_Device_Vulnerability_Software");
        IndexNamesFor<SoftwareSourceIdentity>(db).Should()
            .Contain("UX_SoftwareSourceIdentities_Source_ExternalId");
        IndexNamesFor<SoftwareRelease>(db).Should()
            .Contain("UX_SoftwareReleases_Product_Version");
        IndexNamesFor<IngestionRunDelta>(db).Should()
            .Contain("UX_IngestionRunDeltas_Run_Kind_Id");
    }

    [Fact]
    public void Source_software_identity_factory_collapses_observed_name_to_canonical_product_key()
    {
        var identity = SoftwareSourceIdentity.Create(
            sourceSystemId: Guid.NewGuid(),
            externalId: "defender-sw::edge::126",
            observedVendor: " Microsoft ",
            observedName: " Edge ",
            observedVersion: " 126.0 ",
            observedAt: DateTimeOffset.UtcNow);

        identity.ObservedVendor.Should().Be("Microsoft");
        identity.ObservedName.Should().Be("Edge");
        identity.ObservedVersion.Should().Be("126.0");
        identity.CanonicalProductKey.Should().Be("microsoft::edge");
    }

    private static IReadOnlyList<string?> IndexNamesFor<TEntity>(PatchHoundDbContext db)
        where TEntity : class
    {
        return db.Model.FindEntityType(typeof(TEntity))!
            .GetIndexes()
            .Select(index => index.GetDatabaseName())
            .ToList();
    }
}
