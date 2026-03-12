using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models.Admin;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Secrets;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class TenantsControllerTests : IDisposable
{
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly ISecretStore _secretStore;
    private readonly TenantsController _controller;

    public TenantsControllerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentUserId.Returns(Guid.NewGuid());
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid>());

        var interceptor = new AuditSaveChangesInterceptor(_tenantContext);
        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(_tenantContext)
        );
        _secretStore = Substitute.For<ISecretStore>();
        _controller = new TenantsController(
            _dbContext,
            _secretStore,
            new AuditLogWriter(_dbContext, _tenantContext),
            _tenantContext
        );
    }

    [Fact]
    public async Task Create_CreatesTenantWithDefaultSlaAndDefenderSource()
    {
        var currentUser = User.Create("admin@example.com", "Admin", Guid.NewGuid().ToString());
        _tenantContext.CurrentUserId.Returns(currentUser.Id);
        var existingTenant = Tenant.Create("Existing", "existing-tenant");
        await _dbContext.Tenants.AddAsync(existingTenant);
        await _dbContext.Users.AddAsync(currentUser);
        await _dbContext.UserTenantRoles.AddAsync(
            UserTenantRole.Create(currentUser.Id, existingTenant.Id, RoleName.GlobalAdmin)
        );
        await _dbContext.SaveChangesAsync();
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { existingTenant.Id });
        _tenantContext.CurrentTenantId.Returns(existingTenant.Id);

        var action = await _controller.Create(
            new CreateTenantRequest("Contoso Production", "11111111-1111-1111-1111-111111111111"),
            CancellationToken.None
        );

        var result = action.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var detail = result.Value.Should().BeOfType<TenantDetailDto>().Subject;

        detail.Name.Should().Be("Contoso Production");
        detail.EntraTenantId.Should().Be("11111111-1111-1111-1111-111111111111");
        detail.Sla.CriticalDays.Should().Be(7);
        detail.Sla.HighDays.Should().Be(30);
        detail.Sla.MediumDays.Should().Be(90);
        detail.Sla.LowDays.Should().Be(180);
        detail
            .IngestionSources.Should()
            .ContainSingle(source =>
                source.Key == "microsoft-defender"
                && source.Enabled == false
                && source.Credentials.ApiBaseUrl == "https://api.securitycenter.microsoft.com"
            );

        var role = await _dbContext
            .UserTenantRoles.IgnoreQueryFilters()
            .SingleAsync(item => item.TenantId == detail.Id && item.UserId == currentUser.Id);
        role.Role.Should().Be(RoleName.GlobalAdmin);
    }

    [Fact]
    public async Task TriggerSync_WhenSourceDisabled_ReturnsBadRequest()
    {
        var tenant = Tenant.Create("Contoso", "11111111-1111-1111-1111-111111111111");
        var source = TenantSourceConfiguration.Create(
            tenant.Id,
            "microsoft-defender",
            "Microsoft Defender",
            false,
            "0 */6 * * *",
            "entra-tenant",
            "client-id",
            "tenants/source/secret",
            "https://api.securitycenter.microsoft.com",
            "https://api.securitycenter.microsoft.com/.default"
        );

        await _dbContext.Tenants.AddAsync(tenant);
        await _dbContext.TenantSourceConfigurations.AddAsync(source);
        await _dbContext.SaveChangesAsync();

        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { tenant.Id });
        _tenantContext.HasAccessToTenant(tenant.Id).Returns(true);

        var action = await _controller.TriggerSync(
            tenant.Id,
            source.SourceKey,
            CancellationToken.None
        );

        var badRequest = action.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Title.Should().Be("Enable the source before triggering a manual sync.");
    }

    [Fact]
    public async Task TriggerSync_WhenCredentialsMissing_ReturnsBadRequest()
    {
        var tenant = Tenant.Create("Contoso", "11111111-1111-1111-1111-111111111111");
        var source = TenantSourceConfiguration.Create(
            tenant.Id,
            "microsoft-defender",
            "Microsoft Defender",
            true,
            "0 */6 * * *",
            credentialTenantId: "",
            clientId: "",
            secretRef: "",
            apiBaseUrl: "https://api.securitycenter.microsoft.com",
            tokenScope: "https://api.securitycenter.microsoft.com/.default"
        );

        await _dbContext.Tenants.AddAsync(tenant);
        await _dbContext.TenantSourceConfigurations.AddAsync(source);
        await _dbContext.SaveChangesAsync();

        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { tenant.Id });
        _tenantContext.HasAccessToTenant(tenant.Id).Returns(true);

        var action = await _controller.TriggerSync(
            tenant.Id,
            source.SourceKey,
            CancellationToken.None
        );

        var badRequest = action.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Title.Should().Be("Configure source credentials before triggering a manual sync.");
    }

    [Fact]
    public async Task TriggerSync_WhenSourceIsRunnable_QueuesManualSync()
    {
        var tenant = Tenant.Create("Contoso", "11111111-1111-1111-1111-111111111111");
        var source = TenantSourceConfiguration.Create(
            tenant.Id,
            "microsoft-defender",
            "Microsoft Defender",
            true,
            "0 */6 * * *",
            "entra-tenant",
            "client-id",
            "tenants/source/secret",
            "https://api.securitycenter.microsoft.com",
            "https://api.securitycenter.microsoft.com/.default"
        );

        await _dbContext.Tenants.AddAsync(tenant);
        await _dbContext.TenantSourceConfigurations.AddAsync(source);
        await _dbContext.SaveChangesAsync();

        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { tenant.Id });
        _tenantContext.HasAccessToTenant(tenant.Id).Returns(true);

        var action = await _controller.TriggerSync(
            tenant.Id,
            source.SourceKey,
            CancellationToken.None
        );

        action.Should().BeOfType<AcceptedResult>();

        var updatedSource = await _dbContext.TenantSourceConfigurations.SingleAsync(item =>
            item.Id == source.Id
        );
        updatedSource.ManualRequestedAt.Should().NotBeNull();
        updatedSource.LastStatus.Should().Be("Queued");
    }

    [Fact]
    public async Task ListRuns_ReturnsPagedRunsForSource()
    {
        var tenant = Tenant.Create("Contoso", "11111111-1111-1111-1111-111111111111");
        await _dbContext.Tenants.AddAsync(tenant);

        var firstRun = IngestionRun.Start(
            tenant.Id,
            "microsoft-defender",
            DateTimeOffset.UtcNow.AddMinutes(-20)
        );
        firstRun.CompleteSucceeded(
            DateTimeOffset.UtcNow.AddMinutes(-19),
            10,
            2,
            10,
            5,
            2,
            10
        );

        var secondRun = IngestionRun.Start(
            tenant.Id,
            "microsoft-defender",
            DateTimeOffset.UtcNow.AddMinutes(-10)
        );
        secondRun.CompleteFailed(
            DateTimeOffset.UtcNow.AddMinutes(-9),
            "Ingestion failed: TimeoutException",
            IngestionRunStatuses.FailedRecoverable,
            4,
            1,
            1,
            4,
            2,
            1
        );

        await _dbContext.IngestionRuns.AddRangeAsync(firstRun, secondRun);
        await _dbContext.SaveChangesAsync();

        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { tenant.Id });
        _tenantContext.HasAccessToTenant(tenant.Id).Returns(true);

        var action = await _controller.ListRuns(
            tenant.Id,
            "microsoft-defender",
            new PatchHound.Api.Models.PaginationQuery(1, 10),
            CancellationToken.None
        );

        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok
            .Value.Should()
            .BeOfType<PatchHound.Api.Models.PagedResponse<TenantIngestionRunDto>>()
            .Subject;
        payload.TotalCount.Should().Be(2);
        payload.Items.Should().HaveCount(2);
        payload.Items[0].Status.Should().Be("FailedRecoverable");
        payload.Items[0].Error.Should().Be("Ingestion failed: TimeoutException");
        payload.Items[1].StagedVulnerabilityCount.Should().Be(10);
        payload.Items[1].StagedSoftwareCount.Should().Be(2);
        payload.Items[1].PersistedMachineCount.Should().Be(5);
    }

    [Fact]
    public async Task DeleteRun_RemovesRunAndAssociatedStagedArtifacts()
    {
        var tenant = Tenant.Create("Contoso", "11111111-1111-1111-1111-111111111111");
        var source = TenantSourceConfiguration.Create(
            tenant.Id,
            "microsoft-defender",
            "Microsoft Defender",
            true,
            "0 */6 * * *",
            "entra-tenant",
            "client-id",
            "secret/ref",
            "https://api.securitycenter.microsoft.com",
            "https://api.securitycenter.microsoft.com/.default"
        );
        var run = IngestionRun.Start(tenant.Id, source.SourceKey, DateTimeOffset.UtcNow.AddMinutes(-10));
        run.CompleteFailed(
            DateTimeOffset.UtcNow.AddMinutes(-9),
            "Ingestion failed: TimeoutException",
            IngestionRunStatuses.FailedRecoverable,
            1,
            1,
            1,
            1,
            1,
            1
        );

        await _dbContext.Tenants.AddAsync(tenant);
        await _dbContext.TenantSourceConfigurations.AddAsync(source);
        await _dbContext.IngestionRuns.AddAsync(run);
        var checkpoint = IngestionCheckpoint.Start(
            run.Id,
            tenant.Id,
            source.SourceKey,
            "asset-staging",
            DateTimeOffset.UtcNow.AddMinutes(-9)
        );
        checkpoint.CommitBatch(
            2,
            null,
            1,
            "Running",
            DateTimeOffset.UtcNow.AddMinutes(-9)
        );
        await _dbContext.IngestionCheckpoints.AddAsync(checkpoint);
        await _dbContext.StagedAssets.AddAsync(
            StagedAsset.Create(
                run.Id,
                tenant.Id,
                source.SourceKey,
                "asset-1",
                "host-1",
                AssetType.Device,
                "{}",
                DateTimeOffset.UtcNow.AddMinutes(-10),
                1
            )
        );
        await _dbContext.StagedVulnerabilities.AddAsync(
            StagedVulnerability.Create(
                run.Id,
                tenant.Id,
                source.SourceKey,
                "CVE-2026-0001",
                "Test vulnerability",
                Severity.Critical,
                "{}",
                DateTimeOffset.UtcNow.AddMinutes(-10),
                1
            )
        );
        await _dbContext.StagedVulnerabilityExposures.AddAsync(
            StagedVulnerabilityExposure.Create(
                run.Id,
                tenant.Id,
                source.SourceKey,
                "CVE-2026-0001",
                "asset-1",
                "host-1",
                AssetType.Device,
                "{}",
                DateTimeOffset.UtcNow.AddMinutes(-10),
                1
            )
        );
        await _dbContext.StagedDeviceSoftwareInstallations.AddAsync(
            StagedDeviceSoftwareInstallation.Create(
                run.Id,
                tenant.Id,
                source.SourceKey,
                "asset-1",
                "software-1",
                DateTimeOffset.UtcNow.AddMinutes(-10),
                "{}",
                DateTimeOffset.UtcNow.AddMinutes(-9),
                1
            )
        );
        await _dbContext.SaveChangesAsync();

        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { tenant.Id });
        _tenantContext.HasAccessToTenant(tenant.Id).Returns(true);

        var action = await _controller.DeleteRun(
            tenant.Id,
            source.SourceKey,
            run.Id,
            CancellationToken.None
        );

        action.Should().BeOfType<NoContentResult>();
        (await _dbContext.IngestionRuns.AnyAsync(item => item.Id == run.Id)).Should().BeFalse();
        (await _dbContext.IngestionCheckpoints.AnyAsync(item => item.IngestionRunId == run.Id))
            .Should()
            .BeFalse();
        (await _dbContext.StagedAssets.AnyAsync(item => item.IngestionRunId == run.Id)).Should().BeFalse();
        (await _dbContext.StagedVulnerabilities.AnyAsync(item => item.IngestionRunId == run.Id))
            .Should()
            .BeFalse();
        (
            await _dbContext.StagedVulnerabilityExposures.AnyAsync(item =>
                item.IngestionRunId == run.Id
            )
        )
            .Should()
            .BeFalse();
        (
            await _dbContext.StagedDeviceSoftwareInstallations.AnyAsync(item =>
                item.IngestionRunId == run.Id
            )
        )
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task DeleteRun_WhenRunIsActive_ReturnsConflict()
    {
        var tenant = Tenant.Create("Contoso", "11111111-1111-1111-1111-111111111111");
        var source = TenantSourceConfiguration.Create(
            tenant.Id,
            "microsoft-defender",
            "Microsoft Defender",
            true,
            "0 */6 * * *",
            "entra-tenant",
            "client-id",
            "secret/ref",
            "https://api.securitycenter.microsoft.com",
            "https://api.securitycenter.microsoft.com/.default"
        );
        var run = IngestionRun.Start(tenant.Id, source.SourceKey, DateTimeOffset.UtcNow.AddMinutes(-2));
        source.AcquireLease(run.Id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5));

        await _dbContext.Tenants.AddAsync(tenant);
        await _dbContext.TenantSourceConfigurations.AddAsync(source);
        await _dbContext.IngestionRuns.AddAsync(run);
        await _dbContext.SaveChangesAsync();

        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { tenant.Id });
        _tenantContext.HasAccessToTenant(tenant.Id).Returns(true);

        var action = await _controller.DeleteRun(
            tenant.Id,
            source.SourceKey,
            run.Id,
            CancellationToken.None
        );

        var conflict = action.Should().BeOfType<ConflictObjectResult>().Subject;
        var problem = conflict.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Title.Should().Be("Active ingestion runs cannot be deleted");
        (await _dbContext.IngestionRuns.AnyAsync(item => item.Id == run.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task AbortRun_WhenRunIsActive_RequestsAbort()
    {
        var tenant = Tenant.Create("Contoso", "11111111-1111-1111-1111-111111111111");
        var source = TenantSourceConfiguration.Create(
            tenant.Id,
            "microsoft-defender",
            "Microsoft Defender",
            true,
            "0 */6 * * *",
            "entra-tenant",
            "client-id",
            "secret/ref",
            "https://api.securitycenter.microsoft.com",
            "https://api.securitycenter.microsoft.com/.default"
        );
        var run = IngestionRun.Start(tenant.Id, source.SourceKey, DateTimeOffset.UtcNow.AddMinutes(-2));
        source.AcquireLease(run.Id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5));

        await _dbContext.Tenants.AddAsync(tenant);
        await _dbContext.TenantSourceConfigurations.AddAsync(source);
        await _dbContext.IngestionRuns.AddAsync(run);
        await _dbContext.SaveChangesAsync();

        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { tenant.Id });
        _tenantContext.HasAccessToTenant(tenant.Id).Returns(true);

        var action = await _controller.AbortRun(
            tenant.Id,
            source.SourceKey,
            run.Id,
            CancellationToken.None
        );

        action.Should().BeOfType<AcceptedResult>();
        var updatedRun = await _dbContext.IngestionRuns.SingleAsync(item => item.Id == run.Id);
        updatedRun.AbortRequestedAt.Should().NotBeNull();
        updatedRun.Status.Should().Be(IngestionRunStatuses.FailedTerminal);
        updatedRun.CompletedAt.Should().NotBeNull();
        updatedRun.Error.Should().Be("Ingestion failed: the run was aborted by an operator.");
        var updatedSource = await _dbContext.TenantSourceConfigurations.SingleAsync(item => item.Id == source.Id);
        updatedSource.ActiveIngestionRunId.Should().BeNull();
        updatedSource.LeaseAcquiredAt.Should().BeNull();
        updatedSource.LeaseExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task AbortRun_WhenRunIsIncompleteButNotSourceActive_FinalizesRun()
    {
        var tenant = Tenant.Create("Contoso", "11111111-1111-1111-1111-111111111111");
        var source = TenantSourceConfiguration.Create(
            tenant.Id,
            "microsoft-defender",
            "Microsoft Defender",
            true,
            "0 */6 * * *",
            "entra-tenant",
            "client-id",
            "secret/ref",
            "https://api.securitycenter.microsoft.com",
            "https://api.securitycenter.microsoft.com/.default"
        );
        var run = IngestionRun.Start(tenant.Id, source.SourceKey, DateTimeOffset.UtcNow.AddMinutes(-10));

        await _dbContext.Tenants.AddAsync(tenant);
        await _dbContext.TenantSourceConfigurations.AddAsync(source);
        await _dbContext.IngestionRuns.AddAsync(run);
        await _dbContext.SaveChangesAsync();

        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { tenant.Id });
        _tenantContext.HasAccessToTenant(tenant.Id).Returns(true);

        var action = await _controller.AbortRun(
            tenant.Id,
            source.SourceKey,
            run.Id,
            CancellationToken.None
        );

        action.Should().BeOfType<AcceptedResult>();
        var updatedRun = await _dbContext.IngestionRuns.SingleAsync(item => item.Id == run.Id);
        updatedRun.Status.Should().Be(IngestionRunStatuses.FailedTerminal);
        updatedRun.CompletedAt.Should().NotBeNull();
        updatedRun.Error.Should().Be("Ingestion failed: the run was aborted by an operator.");
    }

    [Fact]
    public async Task AbortRun_WhenRunIsNotActive_ReturnsConflict()
    {
        var tenant = Tenant.Create("Contoso", "11111111-1111-1111-1111-111111111111");
        var source = TenantSourceConfiguration.Create(
            tenant.Id,
            "microsoft-defender",
            "Microsoft Defender",
            true,
            "0 */6 * * *",
            "entra-tenant",
            "client-id",
            "secret/ref",
            "https://api.securitycenter.microsoft.com",
            "https://api.securitycenter.microsoft.com/.default"
        );
        var run = IngestionRun.Start(tenant.Id, source.SourceKey, DateTimeOffset.UtcNow.AddMinutes(-10));
        run.CompleteSucceeded(
            DateTimeOffset.UtcNow.AddMinutes(-9),
            0,
            0,
            0,
            0,
            0,
            0
        );

        await _dbContext.Tenants.AddAsync(tenant);
        await _dbContext.TenantSourceConfigurations.AddAsync(source);
        await _dbContext.IngestionRuns.AddAsync(run);
        await _dbContext.SaveChangesAsync();

        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { tenant.Id });
        _tenantContext.HasAccessToTenant(tenant.Id).Returns(true);

        var action = await _controller.AbortRun(
            tenant.Id,
            source.SourceKey,
            run.Id,
            CancellationToken.None
        );

        var conflict = action.Should().BeOfType<ConflictObjectResult>().Subject;
        var problem = conflict.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Title.Should().Be("Only active ingestion runs can be aborted");
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
