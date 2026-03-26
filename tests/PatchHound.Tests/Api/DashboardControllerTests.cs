using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models.Dashboard;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class DashboardControllerTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly PatchHoundDbContext _dbContext;
    private readonly DashboardController _controller;
    private readonly IRiskChangeBriefAiSummaryService _riskChangeBriefAiSummaryService;
    private readonly ITenantContext _tenantContext;

    public DashboardControllerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { _tenantId });
        _tenantContext.CurrentUserId.Returns(Guid.NewGuid());
        _tenantContext.GetRolesForTenant(_tenantId).Returns([
            RoleName.GlobalAdmin.ToString(),
            RoleName.SecurityManager.ToString(),
            RoleName.TechnicalManager.ToString(),
        ]);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(_tenantContext)
        );
        _riskChangeBriefAiSummaryService = Substitute.For<IRiskChangeBriefAiSummaryService>();
        _riskChangeBriefAiSummaryService
            .GenerateAsync(Arg.Any<Guid>(), Arg.Any<RiskChangeBriefSummaryInput>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        var snapshotResolver = new TenantSnapshotResolver(_dbContext);
        var dashboardQueryService = new PatchHound.Api.Services.DashboardQueryService(
            _dbContext,
            _riskChangeBriefAiSummaryService,
            snapshotResolver
        );
        _controller = new DashboardController(_dbContext, dashboardQueryService, _tenantContext, snapshotResolver);
    }

    [Fact]
    public async Task GetSecurityManagerSummary_ReturnsApprovedDecisions_AndPendingAttentionTasks()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var normalizedSoftware = NormalizedSoftware.Create(
            "Contoso Agent",
            "Contoso",
            "contoso:agent",
            null,
            SoftwareNormalizationMethod.Manual,
            SoftwareNormalizationConfidence.High,
            timestamp
        );
        var tenantSoftware = TenantSoftware.Create(_tenantId, null, normalizedSoftware.Id, timestamp.AddDays(-10), timestamp);
        var softwareAsset = Asset.Create(_tenantId, "soft-sec-1", AssetType.Software, "Contoso Agent 5.2", Criticality.High);
        var vulnerabilityDefinition = VulnerabilityDefinition.Create(
            "CVE-2026-8000",
            "Agent policy exception",
            "Desc",
            Severity.Critical,
            "MicrosoftDefender",
            9.1m
        );
        var decision = RemediationDecision.Create(
            _tenantId,
            tenantSoftware.Id,
            softwareAsset.Id,
            RemediationOutcome.RiskAcceptance,
            "Operations freeze approved by CAB.",
            Guid.NewGuid(),
            expiryDate: timestamp.AddDays(30)
        );
        decision.Approve(Guid.NewGuid());
        var approvalTask = ApprovalTask.Create(
            _tenantId,
            decision.Id,
            RemediationOutcome.RiskAcceptance,
            ApprovalTaskStatus.Pending,
            timestamp.AddHours(12)
        );

        await _dbContext.AddRangeAsync(
            normalizedSoftware,
            tenantSoftware,
            softwareAsset,
            vulnerabilityDefinition,
            decision,
            approvalTask,
            NormalizedSoftwareVulnerabilityProjection.Create(
                _tenantId,
                null,
                tenantSoftware.Id,
                vulnerabilityDefinition.Id,
                SoftwareVulnerabilityMatchMethod.DefenderDirect,
                MatchConfidence.High,
                4,
                3,
                2,
                timestamp.AddDays(-5),
                timestamp,
                null,
                "{}"
            )
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetSecurityManagerSummary(CancellationToken.None);

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<SecurityManagerDashboardSummaryDto>().Subject;

        payload.RecentApprovedDecisions.Should().ContainSingle();
        payload.RecentApprovedDecisions[0].SoftwareName.Should().Be("Contoso Agent");
        payload.RecentApprovedDecisions[0].Outcome.Should().Be(RemediationOutcome.RiskAcceptance.ToString());
        payload.RecentApprovedDecisions[0].HighestSeverity.Should().Be(Severity.Critical.ToString());
        payload.RecentApprovedDecisions[0].VulnerabilityCount.Should().Be(1);

        payload.ApprovalTasksRequiringAttention.Should().ContainSingle();
        payload.ApprovalTasksRequiringAttention[0].SoftwareName.Should().Be("Contoso Agent");
        payload.ApprovalTasksRequiringAttention[0].AttentionState.Should().Be("NearExpiry");
    }

    [Fact]
    public async Task GetOwnerSummary_ReturnsTeamAssignedPatchingTasks_EvenWithoutOwnedAssets()
    {
        var currentUserId = _tenantContext.CurrentUserId;
        var timestamp = DateTimeOffset.UtcNow;
        var team = Team.Create(_tenantId, "Infrastructure Operations");
        var user = User.Create("owner@contoso.local", "Owner User", Guid.NewGuid().ToString());
        var member = TeamMember.Create(team.Id, currentUserId);

        var normalizedSoftware = NormalizedSoftware.Create(
            "Acrobat Dc",
            "Adobe",
            "adobe:acrobat-dc",
            null,
            SoftwareNormalizationMethod.Manual,
            SoftwareNormalizationConfidence.High,
            timestamp
        );
        var tenantSoftware = TenantSoftware.Create(
            _tenantId,
            null,
            normalizedSoftware.Id,
            timestamp.AddDays(-10),
            timestamp
        );
        var softwareAsset = Asset.Create(
            _tenantId,
            "soft-owner-1",
            AssetType.Software,
            "Acrobat Dc",
            Criticality.Medium
        );
        var vulnerabilityDefinition = VulnerabilityDefinition.Create(
            "CVE-2026-8500",
            "Reader RCE",
            "Desc",
            Severity.High,
            "MicrosoftDefender",
            8.8m
        );
        var tenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            vulnerabilityDefinition.Id,
            VulnerabilityStatus.Open,
            timestamp.AddDays(-3)
        );
        var approvedDecision = RemediationDecision.Create(
            _tenantId,
            tenantSoftware.Id,
            softwareAsset.Id,
            RemediationOutcome.ApprovedForPatching,
            "Patch during the next maintenance window.",
            currentUserId
        );
        var patchingTask = PatchingTask.Create(
            _tenantId,
            approvedDecision.Id,
            tenantSoftware.Id,
            softwareAsset.Id,
            team.Id,
            timestamp.AddDays(7)
        );

        await _dbContext.AddRangeAsync(
            user,
            team,
            member,
            normalizedSoftware,
            tenantSoftware,
            softwareAsset,
            vulnerabilityDefinition,
            tenantVulnerability,
            approvedDecision,
            patchingTask,
            SoftwareVulnerabilityMatch.Create(
                _tenantId,
                null,
                softwareAsset.Id,
                vulnerabilityDefinition.Id,
                SoftwareVulnerabilityMatchMethod.DefenderDirect,
                MatchConfidence.High,
                softwareAsset.Name,
                timestamp.AddDays(-3)
            )
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetOwnerSummary(CancellationToken.None);

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<OwnerDashboardSummaryDto>().Subject;

        payload.OwnedAssetCount.Should().Be(0);
        payload.OpenActionCount.Should().Be(1);
        payload.Actions.Should().ContainSingle();
        payload.Actions[0].SoftwareName.Should().Be("Acrobat Dc");
        payload.Actions[0].OwnerTeamName.Should().Be("Infrastructure Operations");
        payload.Actions[0].ActionState.Should().Be(PatchingTaskStatus.Pending.ToString());
    }

    [Fact]
    public async Task GetTechnicalManagerSummary_ReturnsApprovedPatchingTasks_AndDevicesWithAgedPublishedVulnerabilities()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var normalizedSoftware = NormalizedSoftware.Create(
            "Legacy Browser",
            "Fabrikam",
            "fabrikam:browser",
            null,
            SoftwareNormalizationMethod.Manual,
            SoftwareNormalizationConfidence.High,
            timestamp
        );
        var tenantSoftware = TenantSoftware.Create(_tenantId, null, normalizedSoftware.Id, timestamp.AddDays(-20), timestamp);
        var softwareAsset = Asset.Create(_tenantId, "soft-tech-1", AssetType.Software, "Legacy Browser 11", Criticality.Medium);
        var ownerTeam = Team.Create(_tenantId, "Platform Engineering");
        var approvedDecision = RemediationDecision.Create(
            _tenantId,
            tenantSoftware.Id,
            softwareAsset.Id,
            RemediationOutcome.ApprovedForPatching,
            null,
            Guid.NewGuid()
        );
        var pendingDecision = RemediationDecision.Create(
            _tenantId,
            tenantSoftware.Id,
            softwareAsset.Id,
            RemediationOutcome.RiskAcceptance,
            "Awaiting approval for exception.",
            Guid.NewGuid(),
            expiryDate: timestamp.AddDays(14)
        );
        var patchingTask = PatchingTask.Create(
            _tenantId,
            approvedDecision.Id,
            tenantSoftware.Id,
            softwareAsset.Id,
            ownerTeam.Id,
            timestamp.AddDays(7)
        );
        var pendingApprovalTask = ApprovalTask.Create(
            _tenantId,
            pendingDecision.Id,
            RemediationOutcome.RiskAcceptance,
            ApprovalTaskStatus.Pending,
            timestamp.AddHours(8)
        );

        var agedDevice = Asset.Create(_tenantId, "device-aged-1", AssetType.Device, "Device Aged 1", Criticality.Critical);
        agedDevice.UpdateDeviceDetails(
            "aged-1.contoso.local",
            "Active",
            "Windows",
            "11",
            "High",
            timestamp,
            "10.0.0.55",
            "aad-aged-1"
        );
        var agedVulnerabilityDefinition = VulnerabilityDefinition.Create(
            "CVE-2025-1234",
            "Old browser vulnerability",
            "Desc",
            Severity.High,
            "MicrosoftDefender",
            8.4m,
            null,
            timestamp.AddDays(-120)
        );
        var agedTenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            agedVulnerabilityDefinition.Id,
            VulnerabilityStatus.Open,
            timestamp.AddDays(-30)
        );

        await _dbContext.AddRangeAsync(
            normalizedSoftware,
            tenantSoftware,
            softwareAsset,
            ownerTeam,
            approvedDecision,
            pendingDecision,
            patchingTask,
            pendingApprovalTask,
            agedDevice,
            agedVulnerabilityDefinition,
            agedTenantVulnerability,
            NormalizedSoftwareVulnerabilityProjection.Create(
                _tenantId,
                null,
                tenantSoftware.Id,
                agedVulnerabilityDefinition.Id,
                SoftwareVulnerabilityMatchMethod.DefenderDirect,
                MatchConfidence.High,
                6,
                4,
                3,
                timestamp.AddDays(-40),
                timestamp,
                null,
                "{}"
            )
        );
        await _dbContext.VulnerabilityAssetEpisodes.AddAsync(
            VulnerabilityAssetEpisode.Create(
                _tenantId,
                agedTenantVulnerability.Id,
                agedDevice.Id,
                1,
                timestamp.AddDays(-30)
            )
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetTechnicalManagerSummary(CancellationToken.None);

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<TechnicalManagerDashboardSummaryDto>().Subject;

        payload.ApprovedPatchingTasks.Should().ContainSingle();
        payload.ApprovedPatchingTasks[0].SoftwareName.Should().Be("Legacy Browser");
        payload.ApprovedPatchingTasks[0].OwnerTeamName.Should().Be("Platform Engineering");
        payload.ApprovedPatchingTasks[0].AffectedDeviceCount.Should().Be(4);

        payload.DevicesWithAgedVulnerabilities.Should().ContainSingle();
        payload.DevicesWithAgedVulnerabilities[0].DeviceName.Should().Be("aged-1.contoso.local");
        payload.DevicesWithAgedVulnerabilities[0].OldVulnerabilityCount.Should().Be(1);

        payload.ApprovalTasksRequiringAttention.Should().ContainSingle();
        payload.ApprovalTasksRequiringAttention[0].SoftwareName.Should().Be("Legacy Browser");
    }

    [Fact]
    public async Task GetSummary_ComputesRecurringCounts_AndTopRecurringAssets()
    {
        var recurringAsset = Asset.Create(
            _tenantId,
            "device-1",
            AssetType.Device,
            "Device 1",
            Criticality.High
        );
        recurringAsset.UpdateDeviceDetails(
            "device-1.contoso.local",
            "Active",
            "Windows",
            "11",
            "High",
            DateTimeOffset.UtcNow,
            "10.0.0.1",
            "aad-1"
        );

        var otherAsset = Asset.Create(
            _tenantId,
            "device-2",
            AssetType.Device,
            "Device 2",
            Criticality.Medium
        );
        otherAsset.UpdateDeviceDetails(
            "device-2.contoso.local",
            "Active",
            "Windows",
            "11",
            "Medium",
            DateTimeOffset.UtcNow,
            "10.0.0.2",
            "aad-2"
        );

        var recurringDefinition = VulnerabilityDefinition.Create(
            "CVE-2026-1000",
            "Recurring vulnerability",
            "Desc",
            Severity.Critical,
            "MicrosoftDefender",
            9.8m,
            null,
            DateTimeOffset.UtcNow.AddDays(-30)
        );
        var recurringTenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            recurringDefinition.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow
        );

        var nonRecurringDefinition = VulnerabilityDefinition.Create(
            "CVE-2026-1001",
            "Single episode vulnerability",
            "Desc",
            Severity.High,
            "MicrosoftDefender",
            7.1m,
            null,
            DateTimeOffset.UtcNow.AddDays(-15)
        );
        var nonRecurringTenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            nonRecurringDefinition.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow
        );

        var recurringLink = VulnerabilityAsset.Create(
            recurringTenantVulnerability.Id,
            recurringAsset.Id,
            DateTimeOffset.UtcNow.AddDays(-3)
        );
        var nonRecurringLink = VulnerabilityAsset.Create(
            nonRecurringTenantVulnerability.Id,
            otherAsset.Id,
            DateTimeOffset.UtcNow.AddDays(-2)
        );

        await _dbContext.AddRangeAsync(
            recurringAsset,
            otherAsset,
            recurringDefinition,
            nonRecurringDefinition,
            recurringTenantVulnerability,
            nonRecurringTenantVulnerability,
            recurringLink,
            nonRecurringLink
        );

        await _dbContext.VulnerabilityAssetEpisodes.AddRangeAsync(
            VulnerabilityAssetEpisode.Create(
                _tenantId,
                recurringTenantVulnerability.Id,
                recurringAsset.Id,
                1,
                DateTimeOffset.UtcNow.AddDays(-20)
            ),
            VulnerabilityAssetEpisode.Create(
                _tenantId,
                recurringTenantVulnerability.Id,
                recurringAsset.Id,
                2,
                DateTimeOffset.UtcNow.AddDays(-3)
            ),
            VulnerabilityAssetEpisode.Create(
                _tenantId,
                nonRecurringTenantVulnerability.Id,
                otherAsset.Id,
                1,
                DateTimeOffset.UtcNow.AddDays(-2)
            )
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetSummary(
            new DashboardFilterQuery(DeviceGroup: "Tier 0 Servers"),
            CancellationToken.None
        );

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<DashboardSummaryDto>().Subject;

        payload.RecurringVulnerabilityCount.Should().Be(1);
        payload.RecurrenceRatePercent.Should().Be(50.0m);
        payload.TopRecurringVulnerabilities.Should().ContainSingle();
        payload.TopRecurringVulnerabilities[0].ExternalId.Should().Be("CVE-2026-1000");
        payload.TopRecurringVulnerabilities[0].ReappearanceCount.Should().Be(1);
        payload.TopRecurringAssets.Should().ContainSingle();
        payload.TopRecurringAssets[0].Name.Should().Be("device-1.contoso.local");
        payload.TopRecurringAssets[0].RecurringVulnerabilityCount.Should().Be(1);
    }

    [Fact]
    public async Task GetSummary_SeverityCounts_ExcludeResolvedVulnerabilities()
    {
        var openDefinition = VulnerabilityDefinition.Create(
            "CVE-2026-2000",
            "Open vulnerability",
            "Desc",
            Severity.Critical,
            "MicrosoftDefender"
        );
        var openTenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            openDefinition.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow
        );
        var resolvedDefinition = VulnerabilityDefinition.Create(
            "CVE-2026-2001",
            "Resolved vulnerability",
            "Desc",
            Severity.Critical,
            "MicrosoftDefender"
        );
        var resolvedTenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            resolvedDefinition.Id,
            VulnerabilityStatus.Resolved,
            DateTimeOffset.UtcNow
        );

        var openAsset = Asset.Create(
            _tenantId,
            "device-sev-1",
            AssetType.Device,
            "Device Sev 1",
            Criticality.High
        );

        await _dbContext.AddRangeAsync(
            openDefinition,
            resolvedDefinition,
            openTenantVulnerability,
            resolvedTenantVulnerability,
            openAsset
        );
        await _dbContext.VulnerabilityAssetEpisodes.AddAsync(
            VulnerabilityAssetEpisode.Create(
                _tenantId,
                openTenantVulnerability.Id,
                openAsset.Id,
                1,
                DateTimeOffset.UtcNow
            )
        );
        var resolvedEpisode = VulnerabilityAssetEpisode.Create(
            _tenantId,
            resolvedTenantVulnerability.Id,
            openAsset.Id,
            1,
            DateTimeOffset.UtcNow.AddDays(-5)
        );
        resolvedEpisode.Resolve(DateTimeOffset.UtcNow.AddDays(-1));
        await _dbContext.VulnerabilityAssetEpisodes.AddAsync(resolvedEpisode);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetSummary(new DashboardFilterQuery(), CancellationToken.None);

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<DashboardSummaryDto>().Subject;

        payload.VulnerabilitiesBySeverity["Critical"].Should().Be(1);
        payload.VulnerabilitiesByStatus["Resolved"].Should().Be(1);
    }

    [Fact]
    public async Task GetTrends_ExcludesResolvedVulnerabilities_OnResolutionDay()
    {
        var definition = VulnerabilityDefinition.Create(
            "CVE-2026-3000",
            "Recently resolved vulnerability",
            "Desc",
            Severity.High,
            "MicrosoftDefender"
        );
        var tenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            definition.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow
        );
        var asset = Asset.Create(
            _tenantId,
            "device-trend-1",
            AssetType.Device,
            "Device Trend 1",
            Criticality.High
        );
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var firstSeenAt = new DateTimeOffset(
            today.AddDays(-2).ToDateTime(TimeOnly.MinValue),
            TimeSpan.Zero
        );
        var resolvedAt = new DateTimeOffset(
            today.ToDateTime(new TimeOnly(8, 0)),
            TimeSpan.Zero
        );

        await _dbContext.AddRangeAsync(definition, tenantVulnerability, asset);
        await _dbContext.VulnerabilityAssetEpisodes.AddAsync(
            VulnerabilityAssetEpisode.Create(
                _tenantId,
                tenantVulnerability.Id,
                asset.Id,
                1,
                firstSeenAt
            ),
            CancellationToken.None
        );
        await _dbContext.SaveChangesAsync();

        var episode = await _dbContext.VulnerabilityAssetEpisodes.SingleAsync();
        episode.Resolve(resolvedAt);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetTrends(new DashboardFilterQuery(), CancellationToken.None);

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<TrendDataDto>().Subject;

        var todayPoint = payload.Items.Single(item =>
            item.Date == today && item.Severity == Severity.High.ToString()
        );
        var yesterdayPoint = payload.Items.Single(item =>
            item.Date == today.AddDays(-1) && item.Severity == Severity.High.ToString()
        );

        todayPoint.Count.Should().Be(0);
        yesterdayPoint.Count.Should().Be(1);
    }

    [Fact]
    public async Task GetSummary_AndRiskChanges_ReportHighCriticalAppearedAndResolvedItems()
    {
        var appearedDefinition = VulnerabilityDefinition.Create(
            "CVE-2026-4000",
            "Fresh critical issue",
            "Desc",
            Severity.Critical,
            "MicrosoftDefender"
        );
        var appearedTenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            appearedDefinition.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow.AddHours(-2)
        );

        var resolvedDefinition = VulnerabilityDefinition.Create(
            "CVE-2026-4001",
            "Recently resolved issue",
            "Desc",
            Severity.Critical,
            "MicrosoftDefender"
        );
        var resolvedTenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            resolvedDefinition.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow.AddDays(-2)
        );
        resolvedTenantVulnerability.UpdateStatus(
            VulnerabilityStatus.Resolved,
            DateTimeOffset.UtcNow.AddHours(-3)
        );

        var appearedAsset = Asset.Create(
            _tenantId,
            "device-change-1",
            AssetType.Device,
            "Device Change 1",
            Criticality.High
        );
        var resolvedAsset = Asset.Create(
            _tenantId,
            "device-change-2",
            AssetType.Device,
            "Device Change 2",
            Criticality.High
        );

        var appearedEpisode = VulnerabilityAssetEpisode.Create(
            _tenantId,
            appearedTenantVulnerability.Id,
            appearedAsset.Id,
            1,
            DateTimeOffset.UtcNow.AddHours(-2)
        );
        var resolvedEpisode = VulnerabilityAssetEpisode.Create(
            _tenantId,
            resolvedTenantVulnerability.Id,
            resolvedAsset.Id,
            1,
            DateTimeOffset.UtcNow.AddDays(-2)
        );
        resolvedEpisode.Resolve(DateTimeOffset.UtcNow.AddHours(-3));

        await _dbContext.AddRangeAsync(
            appearedDefinition,
            resolvedDefinition,
            appearedTenantVulnerability,
            resolvedTenantVulnerability,
            appearedAsset,
            resolvedAsset,
            VulnerabilityAsset.Create(
                appearedTenantVulnerability.Id,
                appearedAsset.Id,
                DateTimeOffset.UtcNow.AddHours(-2)
            ),
            VulnerabilityAsset.Create(
                resolvedTenantVulnerability.Id,
                resolvedAsset.Id,
                DateTimeOffset.UtcNow.AddDays(-2)
            )
        );
        await _dbContext.VulnerabilityAssetEpisodes.AddRangeAsync(appearedEpisode, resolvedEpisode);
        await _dbContext.SaveChangesAsync();

        var summaryAction = await _controller.GetSummary(new DashboardFilterQuery(), CancellationToken.None);
        var summaryPayload = summaryAction.Result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<DashboardSummaryDto>().Subject;

        summaryPayload.RiskChangeBrief.AppearedCount.Should().Be(1);
        summaryPayload.RiskChangeBrief.ResolvedCount.Should().Be(1);
        summaryPayload.RiskChangeBrief.Appeared.Should().ContainSingle();
        summaryPayload.RiskChangeBrief.Appeared[0].ExternalId.Should().Be("CVE-2026-4000");
        summaryPayload.RiskChangeBrief.Resolved.Should().ContainSingle();
        summaryPayload.RiskChangeBrief.Resolved[0].ExternalId.Should().Be("CVE-2026-4001");

        var detailAction = await _controller.GetRiskChanges(days: 1, ct: CancellationToken.None);
        var detailPayload = detailAction.Result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<DashboardRiskChangeBriefDto>().Subject;

        detailPayload.AppearedCount.Should().Be(1);
        detailPayload.ResolvedCount.Should().Be(1);
    }

    [Fact]
    public async Task GetRiskChanges_IncludesLowerSeverities_WhileSummaryRemainsHighCritical()
    {
        var highDefinition = VulnerabilityDefinition.Create(
            "CVE-2026-4100",
            "High issue",
            "Desc",
            Severity.High,
            "MicrosoftDefender"
        );
        var highTenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            highDefinition.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow.AddHours(-2)
        );

        var mediumDefinition = VulnerabilityDefinition.Create(
            "CVE-2026-4101",
            "Medium issue",
            "Desc",
            Severity.Medium,
            "MicrosoftDefender"
        );
        var mediumTenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            mediumDefinition.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow.AddHours(-1)
        );

        var highAsset = Asset.Create(
            _tenantId,
            "device-sev-high",
            AssetType.Device,
            "Device Sev High",
            Criticality.High
        );
        var mediumAsset = Asset.Create(
            _tenantId,
            "device-sev-medium",
            AssetType.Device,
            "Device Sev Medium",
            Criticality.Medium
        );

        await _dbContext.AddRangeAsync(
            highDefinition,
            mediumDefinition,
            highTenantVulnerability,
            mediumTenantVulnerability,
            highAsset,
            mediumAsset
        );
        await _dbContext.VulnerabilityAssetEpisodes.AddRangeAsync(
            VulnerabilityAssetEpisode.Create(
                _tenantId,
                highTenantVulnerability.Id,
                highAsset.Id,
                1,
                DateTimeOffset.UtcNow.AddHours(-2)
            ),
            VulnerabilityAssetEpisode.Create(
                _tenantId,
                mediumTenantVulnerability.Id,
                mediumAsset.Id,
                1,
                DateTimeOffset.UtcNow.AddHours(-1)
            )
        );
        await _dbContext.SaveChangesAsync();

        var summaryAction = await _controller.GetSummary(new DashboardFilterQuery(), CancellationToken.None);
        var summaryPayload = summaryAction.Result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<DashboardSummaryDto>().Subject;

        summaryPayload.RiskChangeBrief.Appeared.Should().ContainSingle();
        summaryPayload.RiskChangeBrief.Appeared[0].ExternalId.Should().Be("CVE-2026-4100");

        var detailAction = await _controller.GetRiskChanges(days: 1, ct: CancellationToken.None);
        var detailPayload = detailAction.Result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<DashboardRiskChangeBriefDto>().Subject;

        detailPayload.Appeared.Should().HaveCount(2);
        detailPayload.Appeared.Select(item => item.ExternalId)
            .Should()
            .Contain(["CVE-2026-4100", "CVE-2026-4101"]);
    }

    [Fact]
    public async Task GetSummary_IncludesAiSummary_WhenGenerated()
    {
        var definition = VulnerabilityDefinition.Create(
            "CVE-2026-5000",
            "Fresh issue",
            "Desc",
            Severity.Critical,
            "MicrosoftDefender"
        );
        var tenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            definition.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow
        );

        var aiAsset = Asset.Create(
            _tenantId,
            "device-ai-1",
            AssetType.Device,
            "Device AI 1",
            Criticality.High
        );

        await _dbContext.AddRangeAsync(definition, tenantVulnerability, aiAsset);
        await _dbContext.VulnerabilityAssetEpisodes.AddAsync(
            VulnerabilityAssetEpisode.Create(
                _tenantId,
                tenantVulnerability.Id,
                aiAsset.Id,
                1,
                DateTimeOffset.UtcNow
            )
        );
        await _dbContext.SaveChangesAsync();

        _riskChangeBriefAiSummaryService
            .GenerateAsync(_tenantId, Arg.Any<RiskChangeBriefSummaryInput>(), Arg.Any<CancellationToken>())
            .Returns("1 critical issue appeared in the last 24 hours.");

        var action = await _controller.GetSummary(
            new DashboardFilterQuery(DeviceGroup: "Tier 0 Servers"),
            CancellationToken.None
        );

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<DashboardSummaryDto>().Subject;

        payload.RiskChangeBrief.AiSummary.Should().Be("1 critical issue appeared in the last 24 hours.");
    }

    [Fact]
    public async Task GetTrends_FiltersToCurrentTenantOnly()
    {
        var otherTenantId = Guid.NewGuid();

        var currentDefinition = VulnerabilityDefinition.Create(
            "CVE-2026-6000",
            "Current tenant issue",
            "Desc",
            Severity.High,
            "MicrosoftDefender"
        );
        var currentTenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            currentDefinition.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow.AddDays(-1)
        );

        var otherDefinition = VulnerabilityDefinition.Create(
            "CVE-2026-6001",
            "Other tenant issue",
            "Desc",
            Severity.High,
            "MicrosoftDefender"
        );
        var otherTenantVulnerability = TenantVulnerability.Create(
            otherTenantId,
            otherDefinition.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow.AddDays(-1)
        );

        await _dbContext.AddRangeAsync(
            currentDefinition,
            otherDefinition,
            currentTenantVulnerability,
            otherTenantVulnerability
        );
        var currentAsset = Asset.Create(
            _tenantId,
            "trend-current-asset",
            AssetType.Device,
            "Trend Current Asset",
            Criticality.Medium
        );
        currentAsset.UpdateDeviceDetails(
            "trend-current-asset.contoso.local",
            "Active",
            "Windows",
            "11",
            "Medium",
            DateTimeOffset.UtcNow,
            "10.0.0.10",
            "aad-trend-current"
        );
        var otherAsset = Asset.Create(
            otherTenantId,
            "trend-other-asset",
            AssetType.Device,
            "Trend Other Asset",
            Criticality.Medium
        );
        otherAsset.UpdateDeviceDetails(
            "trend-other-asset.contoso.local",
            "Active",
            "Windows",
            "11",
            "Medium",
            DateTimeOffset.UtcNow,
            "10.0.0.11",
            "aad-trend-other"
        );
        await _dbContext.AddRangeAsync(currentAsset, otherAsset);
        await _dbContext.VulnerabilityAssetEpisodes.AddRangeAsync(
            VulnerabilityAssetEpisode.Create(
                _tenantId,
                currentTenantVulnerability.Id,
                currentAsset.Id,
                1,
                DateTimeOffset.UtcNow.AddDays(-1)
            ),
            VulnerabilityAssetEpisode.Create(
                otherTenantId,
                otherTenantVulnerability.Id,
                otherAsset.Id,
                1,
                DateTimeOffset.UtcNow.AddDays(-1)
            )
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetTrends(new DashboardFilterQuery(), CancellationToken.None);

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<TrendDataDto>().Subject;
        var todayHigh = payload.Items.Single(item =>
            item.Date == DateOnly.FromDateTime(DateTime.UtcNow)
            && item.Severity == Severity.High.ToString()
        );

        todayHigh.Count.Should().Be(1);
    }

    [Fact]
    public async Task GetSummary_ReturnsVulnerabilitiesByDeviceGroup()
    {
        var asset1 = Asset.Create(
            _tenantId,
            "device-grp-1",
            AssetType.Device,
            "Device Group 1",
            Criticality.High
        );
        asset1.UpdateDeviceDetails(
            "device-grp-1.contoso.local",
            "Active",
            "Windows",
            "11",
            "High",
            DateTimeOffset.UtcNow,
            "10.0.0.1",
            "aad-grp-1",
            groupId: "grp-tier0",
            groupName: "Tier 0 Servers"
        );

        var asset2 = Asset.Create(
            _tenantId,
            "device-grp-2",
            AssetType.Device,
            "Device Group 2",
            Criticality.Medium
        );
        asset2.UpdateDeviceDetails(
            "device-grp-2.contoso.local",
            "Active",
            "Windows",
            "11",
            "Medium",
            DateTimeOffset.UtcNow,
            "10.0.0.2",
            "aad-grp-2",
            groupId: "grp-field",
            groupName: "Field Devices"
        );

        var definition = VulnerabilityDefinition.Create(
            "CVE-2026-7000",
            "Device group vulnerability",
            "Desc",
            Severity.High,
            "MicrosoftDefender"
        );
        var tenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            definition.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow
        );

        await _dbContext.AddRangeAsync(asset1, asset2, definition, tenantVulnerability);
        await _dbContext.VulnerabilityAssetEpisodes.AddRangeAsync(
            VulnerabilityAssetEpisode.Create(
                _tenantId,
                tenantVulnerability.Id,
                asset1.Id,
                1,
                DateTimeOffset.UtcNow.AddDays(-5)
            ),
            VulnerabilityAssetEpisode.Create(
                _tenantId,
                tenantVulnerability.Id,
                asset2.Id,
                1,
                DateTimeOffset.UtcNow.AddDays(-3)
            )
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetSummary(
            new DashboardFilterQuery(DeviceGroup: "Tier 0 Servers"),
            CancellationToken.None
        );

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<DashboardSummaryDto>().Subject;

        payload.VulnerabilitiesByDeviceGroup.Should().HaveCountGreaterThanOrEqualTo(1);
        payload.VulnerabilitiesByDeviceGroup
            .Select(g => g.DeviceGroupName)
            .Should().Contain("Tier 0 Servers");
    }

    [Fact]
    public async Task GetSummary_ReturnsDeviceHealthBreakdown()
    {
        var activeAsset = Asset.Create(
            _tenantId,
            "device-health-1",
            AssetType.Device,
            "Device Health Active",
            Criticality.High
        );
        activeAsset.UpdateDeviceDetails(
            "device-health-1.contoso.local",
            "Active",
            "Windows",
            "11",
            "High",
            DateTimeOffset.UtcNow,
            "10.0.0.1",
            "aad-health-1"
        );

        var inactiveAsset = Asset.Create(
            _tenantId,
            "device-health-2",
            AssetType.Device,
            "Device Health Inactive",
            Criticality.Medium
        );
        inactiveAsset.UpdateDeviceDetails(
            "device-health-2.contoso.local",
            "Inactive",
            "Windows",
            "11",
            "Medium",
            DateTimeOffset.UtcNow,
            "10.0.0.2",
            "aad-health-2"
        );

        await _dbContext.AddRangeAsync(activeAsset, inactiveAsset);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetSummary(new DashboardFilterQuery(), CancellationToken.None);

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<DashboardSummaryDto>().Subject;

        payload.DeviceHealthBreakdown.Should().ContainKey("Active").WhoseValue.Should().Be(1);
        payload.DeviceHealthBreakdown.Should().ContainKey("Inactive").WhoseValue.Should().Be(1);
    }

    [Fact]
    public async Task GetSummary_FiltersByMinAgeDays()
    {
        var asset = Asset.Create(
            _tenantId,
            "device-age-1",
            AssetType.Device,
            "Device Age 1",
            Criticality.High
        );
        asset.UpdateDeviceDetails(
            "device-age-1.contoso.local",
            "Active",
            "Windows",
            "11",
            "High",
            DateTimeOffset.UtcNow,
            "10.0.0.1",
            "aad-age-1"
        );

        var oldDefinition = VulnerabilityDefinition.Create(
            "CVE-2026-8000",
            "Old vulnerability",
            "Desc",
            Severity.High,
            "MicrosoftDefender",
            publishedDate: DateTimeOffset.UtcNow.AddDays(-100)
        );
        var oldTenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            oldDefinition.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow
        );

        var recentDefinition = VulnerabilityDefinition.Create(
            "CVE-2026-8001",
            "Recent vulnerability",
            "Desc",
            Severity.Medium,
            "MicrosoftDefender",
            publishedDate: DateTimeOffset.UtcNow.AddDays(-10)
        );
        var recentTenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            recentDefinition.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow
        );

        await _dbContext.AddRangeAsync(
            asset,
            oldDefinition,
            recentDefinition,
            oldTenantVulnerability,
            recentTenantVulnerability
        );
        await _dbContext.VulnerabilityAssetEpisodes.AddRangeAsync(
            VulnerabilityAssetEpisode.Create(
                _tenantId,
                oldTenantVulnerability.Id,
                asset.Id,
                1,
                DateTimeOffset.UtcNow.AddDays(-100)
            ),
            VulnerabilityAssetEpisode.Create(
                _tenantId,
                recentTenantVulnerability.Id,
                asset.Id,
                1,
                DateTimeOffset.UtcNow.AddDays(-10)
            )
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetSummary(
            new DashboardFilterQuery(MinAgeDays: 90),
            CancellationToken.None
        );

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<DashboardSummaryDto>().Subject;

        payload.VulnerabilitiesBySeverity.Should().ContainKey("High").WhoseValue.Should().Be(1);
        payload.VulnerabilitiesBySeverity.GetValueOrDefault("Medium", 0).Should().Be(0);
    }

    [Fact]
    public async Task GetSummary_ReturnsDeviceOnboardingBreakdown()
    {
        var onboarded = Asset.Create(_tenantId, "dev-onboarded", AssetType.Device, "Onboarded Device", Criticality.Medium);
        onboarded.UpdateDeviceDetails("onb.local", "Active", "Windows", "11", "Low", DateTimeOffset.UtcNow, "10.0.0.1", null, onboardingStatus: "Onboarded");

        var canBeOnboarded = Asset.Create(_tenantId, "dev-can-onboard", AssetType.Device, "Can Be Onboarded", Criticality.Medium);
        canBeOnboarded.UpdateDeviceDetails("can.local", "Active", "Windows", "11", "Low", DateTimeOffset.UtcNow, "10.0.0.2", null, onboardingStatus: "CanBeOnboarded");

        await _dbContext.AddRangeAsync(onboarded, canBeOnboarded);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetSummary(new DashboardFilterQuery(), CancellationToken.None);
        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<DashboardSummaryDto>().Subject;

        payload.DeviceOnboardingBreakdown.Should().ContainKey("Onboarded").WhoseValue.Should().Be(1);
        payload.DeviceOnboardingBreakdown.Should().ContainKey("CanBeOnboarded").WhoseValue.Should().Be(1);
    }

    [Fact]
    public async Task GetSummary_UsesPersistedDeviceGroupRiskRollups_WhenUnfiltered()
    {
        await _dbContext.AddRangeAsync(
            DeviceGroupRiskScore.Create(
                _tenantId,
                "id:group-1",
                "group-1",
                "Servers",
                780m,
                760m,
                2,
                1,
                0,
                0,
                3,
                3,
                "[]",
                RiskScoreService.CalculationVersion
            ),
            DeviceGroupRiskScore.Create(
                _tenantId,
                "id:group-2",
                "group-2",
                "Workstations",
                300m,
                280m,
                0,
                1,
                1,
                2,
                10,
                4,
                "[]",
                RiskScoreService.CalculationVersion
            )
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetSummary(new DashboardFilterQuery(), CancellationToken.None);
        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<DashboardSummaryDto>().Subject;

        payload.VulnerabilitiesByDeviceGroup.Should().HaveCount(2);
        payload.VulnerabilitiesByDeviceGroup[0].DeviceGroupName.Should().Be("Servers");
        payload.VulnerabilitiesByDeviceGroup[0].CurrentRiskScore.Should().Be(780m);
        payload.VulnerabilitiesByDeviceGroup[0].AssetCount.Should().Be(3);
        payload.VulnerabilitiesByDeviceGroup[0].OpenEpisodeCount.Should().Be(3);
    }

    public void Dispose() => _dbContext.Dispose();
}
