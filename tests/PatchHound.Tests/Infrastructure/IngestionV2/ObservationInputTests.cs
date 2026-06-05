using FluentAssertions;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;

namespace PatchHound.Tests.Infrastructure.IngestionV2;

public sealed class ObservationInputTests
{
    [Fact]
    public void SoftwareObservationInput_create_normalizes_values_and_canonical_key()
    {
        var observedAt = DateTimeOffset.UtcNow;

        var input = SoftwareObservationInput.Create(
            externalId: " defender-sw::edge::126 ",
            vendor: " Microsoft ",
            name: " Edge ",
            version: " 126.0 ",
            observedAt: observedAt,
            batchNumber: 4);

        input.ExternalId.Should().Be("defender-sw::edge::126");
        input.Vendor.Should().Be("Microsoft");
        input.Name.Should().Be("Edge");
        input.Version.Should().Be("126.0");
        input.CanonicalProductKey.Should().Be("microsoft::edge");
        input.ObservedAt.Should().Be(observedAt);
        input.BatchNumber.Should().Be(4);
    }

    [Fact]
    public void IngestionObservationBatch_create_rejects_empty_identifiers()
    {
        var act = () => IngestionObservationBatch.Create(
            tenantId: Guid.Empty,
            ingestionRunId: Guid.NewGuid(),
            sourceSystemId: Guid.NewGuid());

        act.Should().Throw<ArgumentException>()
            .WithMessage("*tenantId*");
    }

    [Fact]
    public void ExposureObservationInput_create_allows_empty_software_for_device_level_findings()
    {
        var input = ExposureObservationInput.Create(
            deviceExternalId: " dev-1 ",
            vulnerabilityExternalId: " CVE-2026-0001 ",
            softwareExternalId: null,
            softwareVersion: null,
            observedAt: DateTimeOffset.UtcNow);

        input.DeviceExternalId.Should().Be("dev-1");
        input.VulnerabilityExternalId.Should().Be("CVE-2026-0001");
        input.SoftwareExternalId.Should().BeEmpty();
        input.SoftwareVersion.Should().BeNull();
    }

    [Fact]
    public void VulnerabilityObservationInput_create_trims_optional_cvss_vector()
    {
        var input = VulnerabilityObservationInput.Create(
            externalId: " CVE-2026-0002 ",
            title: " Test ",
            vendorSeverity: Severity.High,
            observedAt: DateTimeOffset.UtcNow,
            cvssVector: " CVSS:3.1/AV:N ");

        input.ExternalId.Should().Be("CVE-2026-0002");
        input.Title.Should().Be("Test");
        input.CvssVector.Should().Be("CVSS:3.1/AV:N");
    }
}
