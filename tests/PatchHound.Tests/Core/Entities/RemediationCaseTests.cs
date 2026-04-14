using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using Xunit;

namespace PatchHound.Tests.Core.Entities;

public class RemediationCaseTests
{
    [Fact]
    public void Create_sets_tenant_product_and_open_status()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var c = RemediationCase.Create(tenantId, productId);

        Assert.NotEqual(Guid.Empty, c.Id);
        Assert.Equal(tenantId, c.TenantId);
        Assert.Equal(productId, c.SoftwareProductId);
        Assert.Equal(RemediationCaseStatus.Open, c.Status);
        Assert.NotEqual(default, c.CreatedAt);
        Assert.Equal(c.CreatedAt, c.UpdatedAt);
        Assert.Null(c.ClosedAt);
    }

    [Fact]
    public void Create_rejects_empty_tenant()
    {
        Assert.Throws<ArgumentException>(() =>
            RemediationCase.Create(Guid.Empty, Guid.NewGuid()));
    }

    [Fact]
    public void Create_rejects_empty_product()
    {
        Assert.Throws<ArgumentException>(() =>
            RemediationCase.Create(Guid.NewGuid(), Guid.Empty));
    }

    [Fact]
    public void Close_sets_status_and_timestamp()
    {
        var c = RemediationCase.Create(Guid.NewGuid(), Guid.NewGuid());
        c.Close();
        Assert.Equal(RemediationCaseStatus.Closed, c.Status);
        Assert.NotNull(c.ClosedAt);
    }

    [Fact]
    public void Close_is_idempotent()
    {
        var c = RemediationCase.Create(Guid.NewGuid(), Guid.NewGuid());
        c.Close();
        var firstClosedAt = c.ClosedAt;
        c.Close();
        Assert.Equal(firstClosedAt, c.ClosedAt);
    }

    [Fact]
    public void Reopen_from_closed_clears_timestamp()
    {
        var c = RemediationCase.Create(Guid.NewGuid(), Guid.NewGuid());
        c.Close();
        c.Reopen();
        Assert.Equal(RemediationCaseStatus.Open, c.Status);
        Assert.Null(c.ClosedAt);
    }
}
