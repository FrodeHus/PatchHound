using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Infrastructure;

public class CycloneDxSupplyChainImportServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly PatchHoundDbContext _dbContext;
    private readonly CycloneDxSupplyChainImportService _service;

    public CycloneDxSupplyChainImportServiceTests()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.CurrentTenantId.Returns(_tenantId);
        tenantContext.AccessibleTenantIds.Returns([_tenantId]);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(options, TestServiceProviderFactory.Create(tenantContext));
        _service = new CycloneDxSupplyChainImportService(_dbContext);
    }

    [Fact]
    public async Task ImportAsync_DependencyWithoutFixVersion_ClassifiesVendorUpdateRequired()
    {
        var tenantSoftware = await SeedTenantSoftwareAsync();

        var result = await _service.ImportAsync(
            tenantSoftware.Id,
            """
            {
              "bomFormat": "CycloneDX",
              "specVersion": "1.6",
              "metadata": {
                "component": {
                  "bom-ref": "product",
                  "name": "Contoso App",
                  "version": "5.2.0"
                }
              },
              "components": [
                {
                  "bom-ref": "pkg:maven/org.apache.commons/commons-text@1.3.0",
                  "name": "commons-text",
                  "version": "1.3.0"
                }
              ],
              "vulnerabilities": [
                {
                  "id": "CVE-2026-1234",
                  "analysis": {
                    "state": "affected",
                    "detail": "Bundled dependency is affected."
                  },
                  "affects": [
                    { "ref": "pkg:maven/org.apache.commons/commons-text@1.3.0" }
                  ]
                }
              ]
            }
            """,
            CancellationToken.None
        );

        result.RemediationPath.Should().Be(SupplyChainRemediationPath.VendorUpdateRequired);
        result.Confidence.Should().Be(SupplyChainInsightConfidence.Confirmed);
        result.PrimaryComponentName.Should().Be("commons-text");
        result.AffectedVulnerabilityCount.Should().Be(1);

        var softwareProduct = await _dbContext.SoftwareProducts.SingleAsync();
        softwareProduct.SupplyChainRemediationPath.Should().Be(SupplyChainRemediationPath.VendorUpdateRequired);
    }

    [Fact]
    public async Task ImportAsync_RecommendationWithFixedVersion_ClassifiesProductUpgrade()
    {
        var tenantSoftware = await SeedTenantSoftwareAsync();

        var result = await _service.ImportAsync(
            tenantSoftware.Id,
            """
            {
              "bomFormat": "CycloneDX",
              "specVersion": "1.6",
              "metadata": {
                "component": {
                  "bom-ref": "product",
                  "name": "Contoso App",
                  "version": "5.2.0"
                }
              },
              "components": [
                {
                  "bom-ref": "pkg:maven/org.apache.commons/commons-text@1.3.0",
                  "name": "commons-text",
                  "version": "1.3.0"
                }
              ],
              "vulnerabilities": [
                {
                  "id": "CVE-2026-1234",
                  "analysis": {
                    "state": "affected",
                    "detail": "Vendor confirms the product is affected."
                  },
                  "recommendation": "Upgrade to version 5.2.4",
                  "affects": [
                    { "ref": "pkg:maven/org.apache.commons/commons-text@1.3.0" }
                  ]
                }
              ]
            }
            """,
            CancellationToken.None
        );

        result.RemediationPath.Should().Be(SupplyChainRemediationPath.ProductUpgrade);
        result.FixedVersion.Should().Be("5.2.4");
    }

    private async Task<SoftwareTenantRecord> SeedTenantSoftwareAsync()
    {
        var timestamp = new DateTimeOffset(2026, 3, 30, 10, 0, 0, TimeSpan.Zero);
        var softwareProduct = SoftwareProduct.Create("contoso", "contoso-app", null);
        var tenantSoftware = SoftwareTenantRecord.Create(_tenantId, null, softwareProduct.Id, timestamp, timestamp);

        await _dbContext.AddRangeAsync(softwareProduct, tenantSoftware);
        await _dbContext.SaveChangesAsync();

        return tenantSoftware;
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
