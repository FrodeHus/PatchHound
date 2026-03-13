using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class SoftwareControllerTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly PatchHoundDbContext _dbContext;
    private readonly IAiReportProvider _aiProvider;
    private readonly ITenantAiConfigurationResolver _tenantAiConfigurationResolver;
    private readonly TenantAiTextGenerationService _tenantAiTextGenerationService;
    private readonly ITenantAiResearchService _tenantAiResearchService;
    private readonly SoftwareDescriptionJobService _softwareDescriptionJobService;
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

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(tenantContext)
        );
        _aiProvider = Substitute.For<IAiReportProvider>();
        _tenantAiConfigurationResolver = Substitute.For<ITenantAiConfigurationResolver>();
        _tenantAiResearchService = Substitute.For<ITenantAiResearchService>();
        _tenantAiTextGenerationService = new TenantAiTextGenerationService(
            [_aiProvider],
            _tenantAiConfigurationResolver
        );
        _softwareDescriptionJobService = new SoftwareDescriptionJobService(_dbContext);
        _controller = new SoftwareController(
            _dbContext,
            _tenantAiTextGenerationService,
            _softwareDescriptionJobService,
            _tenantAiConfigurationResolver,
            _tenantAiResearchService,
            tenantContext
        );
    }

    [Fact]
    public async Task Get_ReturnsTenantSoftwareDetailWithCohortsAndAliases()
    {
        var graph = await TenantSoftwareGraphFactory.SeedAsync(_dbContext, _tenantId);

        var action = await _controller.Get(graph.TenantSoftware.Id, CancellationToken.None);
        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<TenantSoftwareDetailDto>().Subject;

        payload.Id.Should().Be(graph.TenantSoftware.Id);
        payload.CanonicalName.Should().Be("agent");
        payload.ActiveInstallCount.Should().Be(2);
        payload.UniqueDeviceCount.Should().Be(2);
        payload.ActiveVulnerabilityCount.Should().Be(1);
        payload.VulnerableInstallCount.Should().Be(2);
        payload.VersionCount.Should().Be(2);
        payload.VersionCohorts.Should().HaveCount(2);
        payload.VersionCohorts.Select(item => item.Version).Should().BeEquivalentTo("1.0", "2.0");
        payload.SourceAliases.Should().HaveCount(2);
        payload.Description.Should().BeNull();
    }

    [Fact]
    public async Task GetInstallations_FiltersByVersionAndReturnsPagedRows()
    {
        var graph = await TenantSoftwareGraphFactory.SeedAsync(_dbContext, _tenantId);

        var action = await _controller.GetInstallations(
            graph.TenantSoftware.Id,
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
        var graph = await TenantSoftwareGraphFactory.SeedAsync(_dbContext, _tenantId);

        var action = await _controller.GetVulnerabilities(graph.TenantSoftware.Id, CancellationToken.None);
        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeAssignableTo<IReadOnlyList<TenantSoftwareVulnerabilityDto>>().Subject;

        payload.Should().ContainSingle();
        payload[0].ExternalId.Should().Be("CVE-2026-1000");
        payload[0].AffectedInstallCount.Should().Be(2);
        payload[0].AffectedVersions.Should().BeEquivalentTo("1.0", "2.0");
        payload[0].Evidence.Should().HaveCount(2);
        payload[0].Evidence[0].Method.Should().Be("CpeBinding");
    }

    [Fact]
    public async Task GetVulnerabilities_WhenProjectionIsMissing_ReturnsGroupedMatches()
    {
        var graph = await TenantSoftwareGraphFactory.SeedAsync(_dbContext, _tenantId);

        _dbContext.NormalizedSoftwareVulnerabilityProjections.RemoveRange(
            _dbContext.NormalizedSoftwareVulnerabilityProjections
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetVulnerabilities(graph.TenantSoftware.Id, CancellationToken.None);
        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeAssignableTo<IReadOnlyList<TenantSoftwareVulnerabilityDto>>().Subject;

        payload.Should().ContainSingle();
        payload[0].ExternalId.Should().Be("CVE-2026-1000");
        payload[0].AffectedInstallCount.Should().Be(2);
        payload[0].AffectedVersions.Should().BeEquivalentTo("1.0", "2.0");
        payload[0].Evidence.Should().HaveCount(2);
    }

    [Fact]
    public async Task List_ReturnsPagedNormalizedSoftwareItems()
    {
        var graph = await TenantSoftwareGraphFactory.SeedAsync(_dbContext, _tenantId);

        var action = await _controller.List(
            new TenantSoftwareFilterQuery(Search: "agent", VulnerableOnly: true),
            new PaginationQuery(1, 10),
            CancellationToken.None
        );
        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<PagedResponse<TenantSoftwareListItemDto>>().Subject;

        payload.TotalCount.Should().Be(1);
        payload.Items.Should().ContainSingle();
        payload.Items[0].Id.Should().Be(graph.TenantSoftware.Id);
        payload.Items[0].ActiveInstallCount.Should().Be(2);
        payload.Items[0].ActiveVulnerabilityCount.Should().Be(1);
        payload.Items[0].VersionCount.Should().Be(2);
    }

    [Fact]
    public async Task GenerateAiReport_ReturnsMergedSoftwareReport()
    {
        var graph = await TenantSoftwareGraphFactory.SeedAsync(_dbContext, _tenantId);
        var profile = TenantAiProfileFactory.Create(
            _tenantId,
            providerType: TenantAiProviderType.Ollama,
            name: "Default AI",
            model: "llama3",
            systemPrompt: "system"
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
            graph.TenantSoftware.Id,
            new GenerateTenantSoftwareAiReportRequest(null),
            CancellationToken.None
        );
        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<TenantSoftwareAiReportDto>().Subject;

        payload.TenantSoftwareId.Should().Be(graph.TenantSoftware.Id);
        payload.Content.Should().Be("# Software report");
        payload.ProviderType.Should().Be("Ollama");

        await _aiProvider
            .Received(1)
            .GenerateTextAsync(
                Arg.Is<AiTextGenerationRequest>(request =>
                    request.SystemPrompt.Contains("Do not list individual devices.")
                    && request.SystemPrompt.Contains("How The Vulnerabilities Work")
                    && 
                    request.UserPrompt.Contains("\"software\"")
                    && request.UserPrompt.Contains("\"installationSummary\"")
                    && request.UserPrompt.Contains("\"vulnerabilities\"")
                    && request.UserPrompt.Contains("CVE-2026-1000")
                    && !request.UserPrompt.Contains("\"deviceName\"")
                ),
                Arg.Any<TenantAiProfileResolved>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task GenerateAiReport_WhenProjectionIsMissing_StillIncludesVulnerabilitiesFromMatches()
    {
        var graph = await TenantSoftwareGraphFactory.SeedAsync(_dbContext, _tenantId);
        var profile = TenantAiProfileFactory.Create(
            _tenantId,
            providerType: TenantAiProviderType.Ollama,
            name: "Default AI",
            model: "llama3",
            systemPrompt: "system"
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

        _dbContext.NormalizedSoftwareVulnerabilityProjections.RemoveRange(
            _dbContext.NormalizedSoftwareVulnerabilityProjections
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GenerateAiReport(
            graph.TenantSoftware.Id,
            new GenerateTenantSoftwareAiReportRequest(null),
            CancellationToken.None
        );

        action.Result.Should().BeOfType<OkObjectResult>();

        await _aiProvider
            .Received(1)
            .GenerateTextAsync(
                Arg.Is<AiTextGenerationRequest>(request =>
                    request.UserPrompt.Contains("\"vulnerabilities\"")
                    && request.UserPrompt.Contains("CVE-2026-1000")
                ),
                Arg.Any<TenantAiProfileResolved>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task GenerateDescription_QueuesJob()
    {
        var graph = await TenantSoftwareGraphFactory.SeedAsync(_dbContext, _tenantId);

        var action = await _controller.GenerateDescription(
            graph.TenantSoftware.Id,
            new GenerateTenantSoftwareDescriptionRequest(null),
            CancellationToken.None
        );
        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<TenantSoftwareDescriptionJobDto>().Subject;

        payload.TenantSoftwareId.Should().Be(graph.TenantSoftware.Id);
        payload.Status.Should().Be("Pending");

        var queuedJob = await _dbContext.SoftwareDescriptionJobs.FirstAsync(
            item => item.TenantSoftwareId == graph.TenantSoftware.Id
        );
        queuedJob.Status.Should().Be(SoftwareDescriptionJobStatus.Pending);
    }

    [Fact]
    public async Task GetDescriptionStatus_ReturnsLatestJob()
    {
        var graph = await TenantSoftwareGraphFactory.SeedAsync(_dbContext, _tenantId);
        var job = SoftwareDescriptionJob.Create(
            _tenantId,
            graph.TenantSoftware.Id,
            graph.TenantSoftware.NormalizedSoftwareId,
            null,
            DateTimeOffset.UtcNow.AddMinutes(-1)
        );
        job.Start(DateTimeOffset.UtcNow);
        await _dbContext.SoftwareDescriptionJobs.AddAsync(job);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetDescriptionStatus(graph.TenantSoftware.Id, CancellationToken.None);
        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<TenantSoftwareDescriptionJobDto>().Subject;

        payload.Id.Should().Be(job.Id);
        payload.Status.Should().Be("Running");
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
