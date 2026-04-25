using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models.Admin;
using PatchHound.Api.Models.Credentials;
using PatchHound.Api.Models.System;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Credentials;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Secrets;
using PatchHound.Infrastructure.Services;
using PatchHound.Infrastructure.Tenants;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class SettingsAuditControllerTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly ISecretStore _secretStore;

    public SettingsAuditControllerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { _tenantId });
        _tenantContext.CurrentUserId.Returns(_userId);
        _tenantContext
            .HasAccessToTenant(Arg.Any<Guid>())
            .Returns(callInfo => new List<Guid> { _tenantId }.Contains(callInfo.Arg<Guid>()));

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
        _secretStore
            .PutSecretAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task CreateStoredCredential_WritesAuditEntry_ForCredentialLifecycle()
    {
        var tenant = Tenant.Create("Tenant One", "entra-1");
        _tenantContext.CurrentTenantId.Returns(tenant.Id);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { tenant.Id });
        _tenantContext.HasAccessToTenant(tenant.Id).Returns(true);

        await _dbContext.AddAsync(tenant);
        await _dbContext.SaveChangesAsync();

        var controller = new StoredCredentialsController(
            _dbContext,
            _secretStore,
            _tenantContext,
            new AuditLogWriter(_dbContext, _tenantContext)
        );

        var action = await controller.Create(
            new CreateStoredCredentialRequest(
                "Defender credential",
                "entra-client-secret",
                false,
                "entra-1",
                "client",
                "new-secret",
                [tenant.Id]
            ),
            CancellationToken.None
        );

        action.Result.Should().BeOfType<CreatedAtActionResult>();

        var secretAudit = (await _dbContext
            .AuditLogEntries.IgnoreQueryFilters()
            .Where(entry => entry.EntityType == nameof(StoredCredential))
            .ToListAsync())
            .First(entry => entry.NewValues?.Contains("entra-client-secret") == true);

        secretAudit.TenantId.Should().Be(Guid.Empty);
        secretAudit.Action.Should().Be(AuditAction.Created);
        secretAudit.UserId.Should().Be(_userId);
        secretAudit.NewValues.Should().Contain("Defender credential");
        secretAudit.NewValues.Should().Contain("entra-client-secret");
    }

    [Fact]
    public async Task UpdateEnrichmentSources_WritesGlobalAuditEntry_ForSecretChange()
    {
        var enrichmentSource = EnrichmentSourceConfiguration.Create(
            "nvd",
            "National Vulnerability Database",
            true,
            "system/enrichment-sources/nvd-old",
            "https://services.nvd.nist.gov/rest/json/cves/2.0"
        );

        await _dbContext.EnrichmentSourceConfigurations.AddAsync(enrichmentSource);
        await _dbContext.SaveChangesAsync();

        var controller = new SystemController(
            _secretStore,
            _dbContext,
            new AuditLogWriter(_dbContext, _tenantContext),
            new NotificationEmailConfigurationResolver(
                _secretStore,
                Options.Create(new PatchHound.Infrastructure.Options.SmtpOptions())
            ),
            new MailgunEmailSender(new HttpClient()),
            _tenantContext
        );

        var action = await controller.UpdateEnrichmentSources(
            [
                new UpdateEnrichmentSourceRequest(
                    "nvd",
                    "National Vulnerability Database",
                    true,
                    null,
                    new UpdateEnrichmentSourceCredentialsRequest(
                        null,
                        "replacement-secret",
                        "https://services.nvd.nist.gov/rest/json/cves/2.0"
                    )
                ),
            ],
            CancellationToken.None
        );

        action.Should().BeOfType<NoContentResult>();

        var visibleEntries = await _dbContext.AuditLogEntries.AsNoTracking().ToListAsync();
        visibleEntries
            .Should()
            .ContainSingle(entry => entry.EntityType == "EnrichmentSourceSecret");

        var secretAudit = visibleEntries.Single(entry =>
            entry.EntityType == "EnrichmentSourceSecret"
        );
        secretAudit.TenantId.Should().Be(Guid.Empty);
        secretAudit.Action.Should().Be(AuditAction.Updated);
        secretAudit.UserId.Should().Be(_userId);
        secretAudit.OldValues.Should().Contain("system/enrichment-sources/nvd-old");
        secretAudit.NewValues.Should().Contain("system/enrichment-sources/nvd");
    }

    [Fact]
    public async Task UpdateEnrichmentSources_RejectsTenantScopedStoredCredential_ForGlobalSource()
    {
        var credential = StoredCredential.Create(
            "Tenant scoped",
            StoredCredentialTypes.EntraClientSecret,
            isGlobal: false,
            credentialTenantId: "entra-tenant",
            clientId: "client-id",
            secretRef: "stored-credentials/test",
            now: DateTimeOffset.UtcNow
        );
        credential.TenantScopes.Add(StoredCredentialTenant.Create(credential.Id, _tenantId));
        await _dbContext.StoredCredentials.AddAsync(credential);
        await _dbContext.SaveChangesAsync();

        var controller = new SystemController(
            _secretStore,
            _dbContext,
            new AuditLogWriter(_dbContext, _tenantContext),
            new NotificationEmailConfigurationResolver(
                _secretStore,
                Options.Create(new PatchHound.Infrastructure.Options.SmtpOptions())
            ),
            new MailgunEmailSender(new HttpClient()),
            _tenantContext
        );

        var action = await controller.UpdateEnrichmentSources(
            [
                new UpdateEnrichmentSourceRequest(
                    EnrichmentSourceCatalog.DefenderSourceKey,
                    "Microsoft Defender",
                    true,
                    24,
                    new UpdateEnrichmentSourceCredentialsRequest(
                        credential.Id,
                        string.Empty,
                        "https://api.security.microsoft.com"
                    )
                ),
            ],
            CancellationToken.None
        );

        action.Should().BeOfType<BadRequestObjectResult>();
        (await _dbContext.EnrichmentSourceConfigurations.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UpdateEnrichmentSources_AcceptsGlobalApiKeyStoredCredential_ForNvdSource()
    {
        var credential = StoredCredential.Create(
            "NVD API",
            StoredCredentialTypes.ApiKey,
            isGlobal: true,
            credentialTenantId: string.Empty,
            clientId: string.Empty,
            secretRef: "stored-credentials/nvd",
            now: DateTimeOffset.UtcNow
        );
        await _dbContext.StoredCredentials.AddAsync(credential);
        await _dbContext.SaveChangesAsync();

        var controller = new SystemController(
            _secretStore,
            _dbContext,
            new AuditLogWriter(_dbContext, _tenantContext),
            new NotificationEmailConfigurationResolver(
                _secretStore,
                Options.Create(new PatchHound.Infrastructure.Options.SmtpOptions())
            ),
            new MailgunEmailSender(new HttpClient()),
            _tenantContext
        );

        var action = await controller.UpdateEnrichmentSources(
            [
                new UpdateEnrichmentSourceRequest(
                    EnrichmentSourceCatalog.NvdSourceKey,
                    "NVD API",
                    true,
                    null,
                    new UpdateEnrichmentSourceCredentialsRequest(
                        credential.Id,
                        string.Empty,
                        EnrichmentSourceCatalog.DefaultNvdApiBaseUrl
                    )
                ),
            ],
            CancellationToken.None
        );

        action.Should().BeOfType<NoContentResult>();
        var source = await _dbContext.EnrichmentSourceConfigurations.SingleAsync();
        source.StoredCredentialId.Should().Be(credential.Id);
    }

    [Fact]
    public async Task GetEnrichmentSources_ReturnsQueueSummaryAndRecentRuns()
    {
        var source = EnrichmentSourceConfiguration.Create(
            "nvd",
            "NVD API",
            true,
            "system/enrichment-sources/nvd",
            "https://services.nvd.nist.gov"
        );
        source.UpdateRuntime(
            DateTimeOffset.UtcNow.AddMinutes(-10),
            DateTimeOffset.UtcNow.AddMinutes(-9),
            DateTimeOffset.UtcNow.AddMinutes(-9),
            "Succeeded",
            string.Empty
        );

        var runningJob = EnrichmentJob.Create(
            _tenantId,
            "nvd",
            EnrichmentTargetModel.Vulnerability,
            Guid.NewGuid(),
            "CVE-2026-1000",
            100,
            DateTimeOffset.UtcNow.AddMinutes(-5)
        );
        runningJob.Start(
            "worker",
            DateTimeOffset.UtcNow.AddMinutes(-4),
            DateTimeOffset.UtcNow.AddMinutes(10)
        );

        var pendingJob = EnrichmentJob.Create(
            _tenantId,
            "nvd",
            EnrichmentTargetModel.Vulnerability,
            Guid.NewGuid(),
            "CVE-2026-1001",
            100,
            DateTimeOffset.UtcNow.AddMinutes(-6)
        );

        var run = EnrichmentRun.Start("nvd", DateTimeOffset.UtcNow.AddMinutes(-8));
        run.Complete(
            EnrichmentRunStatus.Succeeded,
            2,
            1,
            0,
            0,
            1,
            DateTimeOffset.UtcNow.AddMinutes(-7)
        );

        await _dbContext.EnrichmentSourceConfigurations.AddAsync(source);
        await _dbContext.EnrichmentJobs.AddRangeAsync(runningJob, pendingJob);
        await _dbContext.EnrichmentRuns.AddAsync(run);
        await _dbContext.SaveChangesAsync();

        var controller = new SystemController(
            _secretStore,
            _dbContext,
            new AuditLogWriter(_dbContext, _tenantContext),
            new NotificationEmailConfigurationResolver(
                _secretStore,
                Options.Create(new PatchHound.Infrastructure.Options.SmtpOptions())
            ),
            new MailgunEmailSender(new HttpClient()),
            _tenantContext
        );

        var action = await controller.GetEnrichmentSources(CancellationToken.None);

        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var sources = ok
            .Value.Should()
            .BeAssignableTo<IReadOnlyList<EnrichmentSourceDto>>()
            .Subject;
        var dto = sources.Should().Contain(s => s.Key == "nvd").Subject;
        dto.Queue.PendingCount.Should().Be(1);
        dto.Queue.RunningCount.Should().Be(1);
        dto.RecentRuns.Should().ContainSingle();
        dto.RecentRuns[0].JobsClaimed.Should().Be(2);
        dto.RecentRuns[0].JobsRetried.Should().Be(1);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private static EnrichmentRun CreateCompletedRun(string sourceKey, int jobsClaimed)
    {
        var run = EnrichmentRun.Start(sourceKey, DateTimeOffset.UtcNow.AddMinutes(-jobsClaimed));
        run.Complete(
            EnrichmentRunStatus.Succeeded,
            jobsClaimed,
            jobsClaimed,
            0,
            0,
            0,
            DateTimeOffset.UtcNow.AddMinutes(-(jobsClaimed - 1))
        );
        return run;
    }
}
