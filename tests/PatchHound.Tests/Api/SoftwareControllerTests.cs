using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Software;
using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Tests.Api;

public class SoftwareControllerTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly PatchHoundDbContext _dbContext;
    private readonly IAiReportProvider _aiProvider;
    private readonly ITenantAiConfigurationResolver _tenantAiConfigurationResolver;
    private readonly TenantAiTextGenerationService _tenantAiTextGenerationService;
    private readonly SoftwareController _controller;

    public SoftwareControllerTests()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.CurrentTenantId.Returns(_tenantId);
        tenantContext.AccessibleTenantIds.Returns([_tenantId]);
        tenantContext.HasAccessToTenant(_tenantId).Returns(true);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(options, BuildServiceProvider(tenantContext));
        _aiProvider = Substitute.For<IAiReportProvider>();
        _tenantAiConfigurationResolver = Substitute.For<ITenantAiConfigurationResolver>();
        _tenantAiTextGenerationService = new TenantAiTextGenerationService(
            [_aiProvider],
            _tenantAiConfigurationResolver
        );
        _controller = new SoftwareController(_dbContext, _tenantAiTextGenerationService, tenantContext);
    }

    [Fact]
    public async Task Get_ReturnsTenantSoftwareDetailWithCohortsAndAliases()
    {
        var tenantSoftware = await SeedNormalizedSoftwareGraphAsync();

        var action = await _controller.Get(tenantSoftware.Id, CancellationToken.None);
        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<TenantSoftwareDetailDto>().Subject;

        payload.Id.Should().Be(tenantSoftware.Id);
        payload.CanonicalName.Should().Be("agent");
        payload.ActiveInstallCount.Should().Be(2);
        payload.UniqueDeviceCount.Should().Be(2);
        payload.ActiveVulnerabilityCount.Should().Be(1);
        payload.VulnerableInstallCount.Should().Be(2);
        payload.VersionCount.Should().Be(2);
        payload.VersionCohorts.Should().HaveCount(2);
        payload.VersionCohorts.Select(item => item.Version).Should().BeEquivalentTo("1.0", "2.0");
        payload.SourceAliases.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetInstallations_FiltersByVersionAndReturnsPagedRows()
    {
        var tenantSoftware = await SeedNormalizedSoftwareGraphAsync();

        var action = await _controller.GetInstallations(
            tenantSoftware.Id,
            new TenantSoftwareInstallationQuery("2.0"),
            new PaginationQuery(1, 10),
            CancellationToken.None
        );
        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<PagedResponse<TenantSoftwareInstallationDto>>().Subject;

        payload.TotalCount.Should().Be(1);
        payload.Items.Should().ContainSingle();
        payload.Items[0].Version.Should().Be("2.0");
        payload.Items[0].DeviceName.Should().Be("Device 2");
        payload.Items[0].OpenVulnerabilityCount.Should().Be(1);
    }

    [Fact]
    public async Task GetVulnerabilities_ReturnsAffectedVersionsAndEvidence()
    {
        var tenantSoftware = await SeedNormalizedSoftwareGraphAsync();

        var action = await _controller.GetVulnerabilities(tenantSoftware.Id, CancellationToken.None);
        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeAssignableTo<IReadOnlyList<TenantSoftwareVulnerabilityDto>>().Subject;

        payload.Should().ContainSingle();
        payload[0].ExternalId.Should().Be("CVE-2026-1000");
        payload[0].AffectedInstallCount.Should().Be(2);
        payload[0].AffectedVersions.Should().BeEquivalentTo("1.0", "2.0");
        payload[0].Evidence.Should().ContainSingle();
        payload[0].Evidence[0].Method.Should().Be("CpeBinding");
    }

    [Fact]
    public async Task List_ReturnsPagedNormalizedSoftwareItems()
    {
        var tenantSoftware = await SeedNormalizedSoftwareGraphAsync();

        var action = await _controller.List(
            new TenantSoftwareFilterQuery(Search: "agent", VulnerableOnly: true),
            new PaginationQuery(1, 10),
            CancellationToken.None
        );
        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<PagedResponse<TenantSoftwareListItemDto>>().Subject;

        payload.TotalCount.Should().Be(1);
        payload.Items.Should().ContainSingle();
        payload.Items[0].Id.Should().Be(tenantSoftware.Id);
        payload.Items[0].ActiveInstallCount.Should().Be(2);
        payload.Items[0].ActiveVulnerabilityCount.Should().Be(1);
        payload.Items[0].VersionCount.Should().Be(2);
    }

    [Fact]
    public async Task GenerateAiReport_ReturnsMergedSoftwareReport()
    {
        var tenantSoftware = await SeedNormalizedSoftwareGraphAsync();
        var profile = TenantAiProfile.Create(
            _tenantId,
            "Default AI",
            TenantAiProviderType.Ollama,
            true,
            true,
            "llama3",
            "system",
            0.2m,
            null,
            1200,
            60
        );
        _tenantAiConfigurationResolver
            .ResolveDefaultAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(
                Result<TenantAiProfileResolved>.Success(new TenantAiProfileResolved(profile, string.Empty))
            );
        _aiProvider.ProviderType.Returns(TenantAiProviderType.Ollama);
        _aiProvider
            .ValidateAsync(Arg.Any<TenantAiProfileResolved>(), Arg.Any<CancellationToken>())
            .Returns(AiProviderValidationResult.Success());
        _aiProvider
            .GenerateTextAsync(Arg.Any<AiTextGenerationRequest>(), Arg.Any<TenantAiProfileResolved>(), Arg.Any<CancellationToken>())
            .Returns("# Software report");

        var action = await _controller.GenerateAiReport(
            tenantSoftware.Id,
            new GenerateTenantSoftwareAiReportRequest(null),
            CancellationToken.None
        );
        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<TenantSoftwareAiReportDto>().Subject;

        payload.TenantSoftwareId.Should().Be(tenantSoftware.Id);
        payload.Content.Should().Be("# Software report");
        payload.ProviderType.Should().Be("Ollama");

        await _aiProvider
            .Received(1)
            .GenerateTextAsync(
                Arg.Is<AiTextGenerationRequest>(request =>
                    request.UserPrompt.Contains("\"software\"")
                    && request.UserPrompt.Contains("\"installations\"")
                    && request.UserPrompt.Contains("\"vulnerabilities\"")
                    && request.UserPrompt.Contains("CVE-2026-1000")
                ),
                Arg.Any<TenantAiProfileResolved>(),
                Arg.Any<CancellationToken>()
            );
    }

    private async Task<TenantSoftware> SeedNormalizedSoftwareGraphAsync()
    {
        var normalizedSoftware = NormalizedSoftware.Create(
            "agent",
            "contoso",
            "cpe:contoso:agent",
            "cpe:2.3:a:contoso:agent:*:*:*:*:*:*:*:*",
            SoftwareNormalizationMethod.ExplicitCpe,
            SoftwareNormalizationConfidence.High,
            new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.Zero)
        );
        var tenantSoftware = TenantSoftware.Create(
            _tenantId,
            normalizedSoftware.Id,
            new DateTimeOffset(2026, 2, 10, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero)
        );

        var deviceOne = Asset.Create(_tenantId, "device-1", AssetType.Device, "Device 1", Criticality.High);
        deviceOne.UpdateDeviceDetails("Device 1", null, null, null, null, null, null, null);
        var deviceTwo = Asset.Create(_tenantId, "device-2", AssetType.Device, "Device 2", Criticality.Medium);
        deviceTwo.UpdateDeviceDetails("Device 2", null, null, null, null, null, null, null);
        var softwareOne = Asset.Create(_tenantId, "software-1", AssetType.Software, "Contoso Agent", Criticality.Low);
        var softwareTwo = Asset.Create(_tenantId, "software-2", AssetType.Software, "Contoso Agent", Criticality.Low);
        var profile = AssetSecurityProfile.Create(
            _tenantId,
            "Server Profile",
            null,
            EnvironmentClass.Server,
            InternetReachability.InternalNetwork,
            SecurityRequirementLevel.High,
            SecurityRequirementLevel.High,
            SecurityRequirementLevel.High
        );
        deviceOne.AssignSecurityProfile(profile.Id);

        var definition = VulnerabilityDefinition.Create(
            "CVE-2026-1000",
            "Contoso Agent vulnerability",
            "Description",
            Severity.Critical,
            "NVD",
            9.8m,
            null,
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero)
        );
        var tenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            definition.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow
        );

        await _dbContext.AddRangeAsync(
            normalizedSoftware,
            tenantSoftware,
            deviceOne,
            deviceTwo,
            softwareOne,
            softwareTwo,
            profile,
            definition,
            tenantVulnerability
        );
        await _dbContext.NormalizedSoftwareAliases.AddRangeAsync(
            NormalizedSoftwareAlias.Create(
                normalizedSoftware.Id,
                SoftwareIdentitySourceSystem.Defender,
                "software-1",
                "Contoso Agent",
                "Contoso",
                "1.0",
                SoftwareNormalizationConfidence.High,
                "Resolved via software CPE binding.",
                DateTimeOffset.UtcNow
            ),
            NormalizedSoftwareAlias.Create(
                normalizedSoftware.Id,
                SoftwareIdentitySourceSystem.Defender,
                "software-2",
                "Contoso Agent",
                "Contoso",
                "2.0",
                SoftwareNormalizationConfidence.High,
                "Resolved via software CPE binding.",
                DateTimeOffset.UtcNow
            )
        );
        await _dbContext.NormalizedSoftwareInstallations.AddRangeAsync(
            NormalizedSoftwareInstallation.Create(
                _tenantId,
                tenantSoftware.Id,
                softwareOne.Id,
                deviceOne.Id,
                SoftwareIdentitySourceSystem.Defender,
                "1.0",
                new DateTimeOffset(2026, 2, 10, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero),
                null,
                true,
                1
            ),
            NormalizedSoftwareInstallation.Create(
                _tenantId,
                tenantSoftware.Id,
                softwareTwo.Id,
                deviceTwo.Id,
                SoftwareIdentitySourceSystem.Defender,
                "2.0",
                new DateTimeOffset(2026, 2, 15, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 3, 9, 0, 0, 0, TimeSpan.Zero),
                null,
                true,
                1
            )
        );
        await _dbContext.SoftwareVulnerabilityMatches.AddRangeAsync(
            SoftwareVulnerabilityMatch.Create(
                _tenantId,
                softwareOne.Id,
                definition.Id,
                SoftwareVulnerabilityMatchMethod.CpeBinding,
                MatchConfidence.High,
                "match-one",
                new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero)
            ),
            SoftwareVulnerabilityMatch.Create(
                _tenantId,
                softwareTwo.Id,
                definition.Id,
                SoftwareVulnerabilityMatchMethod.CpeBinding,
                MatchConfidence.High,
                "match-two",
                new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero)
            )
        );
        await _dbContext.NormalizedSoftwareVulnerabilityProjections.AddAsync(
            NormalizedSoftwareVulnerabilityProjection.Create(
                _tenantId,
                tenantSoftware.Id,
                definition.Id,
                SoftwareVulnerabilityMatchMethod.CpeBinding,
                MatchConfidence.High,
                2,
                2,
                2,
                new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero),
                null,
                """
                [{"method":"CpeBinding","confidence":"High","evidence":"contoso-agent","firstSeenAt":"2026-03-10T00:00:00+00:00","lastSeenAt":"2026-03-10T00:00:00+00:00","resolvedAt":null}]
                """
            )
        );
        await _dbContext.VulnerabilityAssets.AddRangeAsync(
            VulnerabilityAsset.Create(
                tenantVulnerability.Id,
                deviceOne.Id,
                new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero)
            ),
            VulnerabilityAsset.Create(
                tenantVulnerability.Id,
                deviceTwo.Id,
                new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero)
            )
        );
        await _dbContext.SaveChangesAsync();

        return tenantSoftware;
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private static IServiceProvider BuildServiceProvider(ITenantContext tenantContext)
    {
        var services = new ServiceCollection();
        services.AddSingleton(tenantContext);
        return services.BuildServiceProvider();
    }
}
