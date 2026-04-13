using PatchHound.Core.Entities;
using Xunit;

namespace PatchHound.Tests.Core;

public class TenantSoftwareProductInsightTests
{
    [Fact]
    public void Create_sets_identity_and_timestamps()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var insight = TenantSoftwareProductInsight.Create(tenantId, productId);
        Assert.Equal(tenantId, insight.TenantId);
        Assert.Equal(productId, insight.SoftwareProductId);
        Assert.Null(insight.Description);
    }

    [Fact]
    public void UpdateDescription_sets_description_and_bumps_updated_at()
    {
        var insight = TenantSoftwareProductInsight.Create(Guid.NewGuid(), Guid.NewGuid());
        var before = insight.UpdatedAt;
        Thread.Sleep(5);
        insight.UpdateDescription("tenant-specific notes");
        Assert.Equal("tenant-specific notes", insight.Description);
        Assert.True(insight.UpdatedAt > before);
    }

    [Fact]
    public void Create_rejects_empty_tenantId()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            TenantSoftwareProductInsight.Create(Guid.Empty, Guid.NewGuid()));
        Assert.Equal("tenantId", ex.ParamName);
    }

    [Fact]
    public void Create_rejects_empty_softwareProductId()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            TenantSoftwareProductInsight.Create(Guid.NewGuid(), Guid.Empty));
        Assert.Equal("softwareProductId", ex.ParamName);
    }

    [Fact]
    public void UpdateDescription_rejects_description_longer_than_4096_chars()
    {
        var insight = TenantSoftwareProductInsight.Create(Guid.NewGuid(), Guid.NewGuid());
        var ex = Assert.Throws<ArgumentException>(() => insight.UpdateDescription(new string('a', 4097)));
        Assert.Equal("description", ex.ParamName);
    }

    [Fact]
    public void UpdateDescription_accepts_null_to_clear()
    {
        var insight = TenantSoftwareProductInsight.Create(Guid.NewGuid(), Guid.NewGuid());
        insight.UpdateDescription("x");
        var before = insight.UpdatedAt;
        Thread.Sleep(5);
        insight.UpdateDescription(null);
        Assert.Null(insight.Description);
        Assert.True(insight.UpdatedAt > before);
    }

    [Fact]
    public void UpdateSupplyChainEvidence_bumps_updated_at_and_round_trips_null()
    {
        var insight = TenantSoftwareProductInsight.Create(Guid.NewGuid(), Guid.NewGuid());
        insight.UpdateSupplyChainEvidence("{\"source\":\"nvd\"}");
        Assert.Equal("{\"source\":\"nvd\"}", insight.SupplyChainEvidenceJson);
        var before = insight.UpdatedAt;
        Thread.Sleep(5);
        insight.UpdateSupplyChainEvidence(null);
        Assert.Null(insight.SupplyChainEvidenceJson);
        Assert.True(insight.UpdatedAt > before);
    }
}
