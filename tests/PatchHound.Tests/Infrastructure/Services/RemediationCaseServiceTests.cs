using Microsoft.EntityFrameworkCore;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.Infrastructure;
using PatchHound.Tests.TestData;
using Xunit;

namespace PatchHound.Tests.Infrastructure.Services;

public class RemediationCaseServiceTests
{
    [Fact]
    public async Task GetOrCreate_returns_existing_case_for_same_tenant_and_product()
    {
        using var ctx = TestDbContextFactory.CreateTenantContext(out var tenantId);
        var product = CanonicalTestData.Product();
        ctx.SoftwareProducts.Add(product);
        await ctx.SaveChangesAsync();

        var sut = new RemediationCaseService(ctx);
        var first = await sut.GetOrCreateAsync(tenantId, product.Id, CancellationToken.None);
        var second = await sut.GetOrCreateAsync(tenantId, product.Id, CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await ctx.RemediationCases.CountAsync());
    }

    [Fact]
    public async Task GetOrCreate_creates_separate_case_per_tenant()
    {
        using var ctx = TestDbContextFactory.CreateSystemContext();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var product = CanonicalTestData.Product();
        ctx.SoftwareProducts.Add(product);
        await ctx.SaveChangesAsync();

        var sut = new RemediationCaseService(ctx);
        var a = await sut.GetOrCreateAsync(tenantA, product.Id, CancellationToken.None);
        var b = await sut.GetOrCreateAsync(tenantB, product.Id, CancellationToken.None);

        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(tenantA, a.TenantId);
        Assert.Equal(tenantB, b.TenantId);
    }

    [Fact]
    public async Task GetOrCreate_rejects_unknown_product()
    {
        using var ctx = TestDbContextFactory.CreateTenantContext(out var tenantId);
        var sut = new RemediationCaseService(ctx);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.GetOrCreateAsync(tenantId, Guid.NewGuid(), CancellationToken.None));
    }
}
