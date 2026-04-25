using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services.Inventory;

namespace PatchHound.Tests.Infrastructure.Services;

public class SoftwareProductResolverTests : IAsyncLifetime
{
    private PatchHoundDbContext _db = null!;
    private SourceSystem _sourceSystem = null!;
    private SoftwareProductResolver _sut = null!;

    public async ValueTask InitializeAsync()
    {
        _db = await TestDbContextFactory.CreateAsync();
        _sourceSystem = SourceSystem.Create("defender", "Defender");
        _db.SourceSystems.Add(_sourceSystem);
        await _db.SaveChangesAsync();

        _sut = new SoftwareProductResolver(_db);
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Resolve_creates_product_and_alias_on_first_observation()
    {
        var observation = new SoftwareObservation(
            SourceSystemId: _sourceSystem.Id,
            ExternalId: "sw-001",
            Vendor: "Mozilla",
            Name: "Firefox");

        var product = await _sut.ResolveAsync(observation, CancellationToken.None);

        product.Should().NotBeNull();
        product.Vendor.Should().Be("Mozilla");
        product.Name.Should().Be("Firefox");
        product.CanonicalProductKey.Should().Be("mozilla::firefox");

        var allProducts = await _db.SoftwareProducts.ToListAsync();
        allProducts.Should().ContainSingle();

        var allAliases = await _db.SoftwareAliases.ToListAsync();
        allAliases.Should().ContainSingle();
        var alias = allAliases.Single();
        alias.SoftwareProductId.Should().Be(product.Id);
        alias.SourceSystemId.Should().Be(_sourceSystem.Id);
        alias.ExternalId.Should().Be("sw-001");
        alias.ObservedVendor.Should().Be("Mozilla");
        alias.ObservedName.Should().Be("Firefox");
    }

    [Fact]
    public async Task Resolve_returns_existing_product_on_second_observation()
    {
        var observation = new SoftwareObservation(
            SourceSystemId: _sourceSystem.Id,
            ExternalId: "sw-002",
            Vendor: "Mozilla",
            Name: "Firefox");

        var first = await _sut.ResolveAsync(observation, CancellationToken.None);
        var second = await _sut.ResolveAsync(observation, CancellationToken.None);

        second.Id.Should().Be(first.Id);

        var allProducts = await _db.SoftwareProducts.ToListAsync();
        allProducts.Should().ContainSingle();

        var allAliases = await _db.SoftwareAliases.ToListAsync();
        allAliases.Should().ContainSingle();
    }

    [Fact]
    public async Task Resolve_different_external_ids_same_canonical_resolve_to_same_product()
    {
        var observation1 = new SoftwareObservation(
            SourceSystemId: _sourceSystem.Id,
            ExternalId: "sw-A",
            Vendor: "Mozilla",
            Name: "Firefox");
        var observation2 = new SoftwareObservation(
            SourceSystemId: _sourceSystem.Id,
            ExternalId: "sw-B",
            Vendor: "mozilla",
            Name: "firefox");

        var first = await _sut.ResolveAsync(observation1, CancellationToken.None);
        var second = await _sut.ResolveAsync(observation2, CancellationToken.None);

        second.Id.Should().Be(first.Id);

        var allProducts = await _db.SoftwareProducts.ToListAsync();
        allProducts.Should().ContainSingle();

        var allAliases = await _db.SoftwareAliases.ToListAsync();
        allAliases.Should().HaveCount(2);
        allAliases.Select(a => a.ExternalId).Should().BeEquivalentTo(new[] { "sw-A", "sw-B" });
        allAliases.Should().OnlyContain(a => a.SoftwareProductId == first.Id);
    }

    [Fact]
    public async Task Resolve_derives_primary_cpe_from_vendor_and_name()
    {
        var observation = new SoftwareObservation(
            SourceSystemId: _sourceSystem.Id,
            ExternalId: "sw-003",
            Vendor: "Microsoft",
            Name: "Edge");

        var product = await _sut.ResolveAsync(observation, CancellationToken.None);

        product.PrimaryCpe23Uri.Should().Be("cpe:2.3:a:microsoft:edge:*:*:*:*:*:*:*:*");
    }

    [Fact]
    public async Task Resolve_reuses_existing_product_cpe_unchanged_on_second_observation()
    {
        // First observation creates the product with a derived CPE.
        var observation = new SoftwareObservation(
            SourceSystemId: _sourceSystem.Id,
            ExternalId: "sw-004",
            Vendor: "Google",
            Name: "Chrome");

        var first = await _sut.ResolveAsync(observation, CancellationToken.None);
        first.PrimaryCpe23Uri.Should().Be("cpe:2.3:a:google:chrome:*:*:*:*:*:*:*:*");

        // Second observation should return the same product without overwriting the CPE.
        var second = await _sut.ResolveAsync(observation, CancellationToken.None);
        second.Id.Should().Be(first.Id);
        second.PrimaryCpe23Uri.Should().Be("cpe:2.3:a:google:chrome:*:*:*:*:*:*:*:*");
    }

    [Theory]
    [InlineData("Microsoft", "Edge", "cpe:2.3:a:microsoft:edge:*:*:*:*:*:*:*:*")]
    [InlineData("mozilla", "firefox", "cpe:2.3:a:mozilla:firefox:*:*:*:*:*:*:*:*")]
    [InlineData("  Acme Corp  ", "  My App  ", "cpe:2.3:a:acme corp:my app:*:*:*:*:*:*:*:*")]
    public void DeriveCpe_normalises_vendor_and_name(string vendor, string name, string expected)
    {
        SoftwareProductResolver.DeriveCpe(vendor, name).Should().Be(expected);
    }
}
