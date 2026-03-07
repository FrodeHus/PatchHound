using FluentAssertions;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;

namespace PatchHound.Tests.Core;

public class AiReportServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    private readonly IAiReportProvider _azureProvider;
    private readonly AiReportService _service;

    public AiReportServiceTests()
    {
        _azureProvider = Substitute.For<IAiReportProvider>();
        _azureProvider.ProviderName.Returns("AzureOpenAI");

        _service = new AiReportService(new[] { _azureProvider });
    }

    [Fact]
    public async Task GenerateReport_ProviderFound_ReturnsAIReport()
    {
        var vulnerability = Vulnerability.Create(
            _tenantId,
            "CVE-2025-1234",
            "Test Vulnerability",
            "A critical vulnerability",
            Severity.Critical,
            "Defender",
            9.8m
        );

        var assets = new List<Asset>
        {
            Asset.Create(_tenantId, "asset-1", AssetType.Device, "web-server-01", Criticality.High),
        };

        _azureProvider
            .GenerateReportAsync(
                vulnerability,
                Arg.Any<IReadOnlyList<Asset>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns("# AI Report\n\nThis is a generated report.");

        var result = await _service.GenerateReportAsync(
            vulnerability,
            assets,
            _tenantId,
            _userId,
            "AzureOpenAI",
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.VulnerabilityId.Should().Be(vulnerability.Id);
        result.Value.TenantId.Should().Be(_tenantId);
        result.Value.GeneratedBy.Should().Be(_userId);
        result.Value.Provider.Should().Be("AzureOpenAI");
        result.Value.Content.Should().Be("# AI Report\n\nThis is a generated report.");
        result.Value.Id.Should().NotBeEmpty();
        result.Value.GeneratedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GenerateReport_UnknownProvider_ReturnsFailure()
    {
        var vulnerability = Vulnerability.Create(
            _tenantId,
            "CVE-2025-1234",
            "Test Vulnerability",
            "A critical vulnerability",
            Severity.Critical,
            "Defender"
        );

        var result = await _service.GenerateReportAsync(
            vulnerability,
            new List<Asset>(),
            _tenantId,
            _userId,
            "NonExistentProvider",
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Unknown AI provider");
        result.Error.Should().Contain("NonExistentProvider");
    }

    [Fact]
    public async Task GenerateReport_ProviderNameIsCaseInsensitive()
    {
        var vulnerability = Vulnerability.Create(
            _tenantId,
            "CVE-2025-5678",
            "Another Vulnerability",
            "Description",
            Severity.High,
            "Qualys"
        );

        _azureProvider
            .GenerateReportAsync(
                vulnerability,
                Arg.Any<IReadOnlyList<Asset>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns("Report content");

        var result = await _service.GenerateReportAsync(
            vulnerability,
            new List<Asset>(),
            _tenantId,
            _userId,
            "azureopenai",
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Content.Should().Be("Report content");
    }

    [Fact]
    public async Task GenerateReport_MultipleProviders_SelectsCorrectOne()
    {
        var anthropicProvider = Substitute.For<IAiReportProvider>();
        anthropicProvider.ProviderName.Returns("Anthropic");
        anthropicProvider
            .GenerateReportAsync(
                Arg.Any<Vulnerability>(),
                Arg.Any<IReadOnlyList<Asset>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns("Anthropic report");

        _azureProvider
            .GenerateReportAsync(
                Arg.Any<Vulnerability>(),
                Arg.Any<IReadOnlyList<Asset>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns("Azure report");

        var service = new AiReportService(new[] { _azureProvider, anthropicProvider });

        var vulnerability = Vulnerability.Create(
            _tenantId,
            "CVE-2025-9999",
            "Test",
            "Desc",
            Severity.Medium,
            "Scanner"
        );

        var result = await service.GenerateReportAsync(
            vulnerability,
            new List<Asset>(),
            _tenantId,
            _userId,
            "Anthropic",
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Content.Should().Be("Anthropic report");
        result.Value.Provider.Should().Be("Anthropic");
    }
}
