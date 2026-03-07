using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models.Admin;
using PatchHound.Api.Models.System;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Secrets;
using PatchHound.Infrastructure.Services;

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

        var interceptor = new AuditSaveChangesInterceptor(_tenantContext);
        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;

        _dbContext = new PatchHoundDbContext(options, BuildServiceProvider(_tenantContext));
        _secretStore = Substitute.For<ISecretStore>();
        _secretStore.PutSecretAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task UpdateTenant_WritesAuditEntry_ForTenantSourceSecretChange()
    {
        var tenant = Tenant.Create("Tenant One", "entra-1");
        _tenantContext.CurrentTenantId.Returns(tenant.Id);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { tenant.Id });
        var source = TenantSourceConfiguration.Create(
            tenant.Id,
            "microsoft-defender",
            "Microsoft Defender",
            true,
            "0 */6 * * *",
            "tenant",
            "client",
            "tenants/old/source",
            "https://api.security.microsoft.com",
            "scope"
        );

        await _dbContext.AddRangeAsync(tenant, source);
        await _dbContext.SaveChangesAsync();

        var controller = new TenantsController(
            _dbContext,
            _secretStore,
            new AuditLogWriter(_dbContext, _tenantContext)
        );

        var action = await controller.Update(
            tenant.Id,
            new UpdateTenantRequest(
                "Tenant One Updated",
                new UpdateTenantSlaConfigurationRequest(5, 10, 20, 30),
                [
                    new UpdateTenantIngestionSourceRequest(
                        "microsoft-defender",
                        "Microsoft Defender",
                        true,
                        "0 */12 * * *",
                        new UpdateTenantSourceCredentialsRequest(
                            "tenant",
                            "client",
                            "new-secret",
                            "https://api.security.microsoft.com",
                            "scope"
                        )
                    ),
                ]
            ),
            CancellationToken.None
        );

        action.Should().BeOfType<NoContentResult>();

        var secretAudit = await _dbContext.AuditLogEntries.IgnoreQueryFilters()
            .SingleAsync(entry => entry.EntityType == "TenantSourceSecret");

        secretAudit.TenantId.Should().Be(tenant.Id);
        secretAudit.Action.Should().Be(AuditAction.Updated);
        secretAudit.UserId.Should().Be(_userId);
        secretAudit.OldValues.Should().Contain("tenants/old/source");
        secretAudit.NewValues.Should().Contain($"tenants/{tenant.Id}/sources/microsoft-defender");
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
            new AuditLogWriter(_dbContext, _tenantContext)
        );

        var action = await controller.UpdateEnrichmentSources(
            [
                new UpdateEnrichmentSourceRequest(
                    "nvd",
                    "National Vulnerability Database",
                    true,
                    new UpdateEnrichmentSourceCredentialsRequest(
                        "replacement-secret",
                        "https://services.nvd.nist.gov/rest/json/cves/2.0"
                    )
                ),
            ],
            CancellationToken.None
        );

        action.Should().BeOfType<NoContentResult>();

        var visibleEntries = await _dbContext.AuditLogEntries.AsNoTracking().ToListAsync();
        visibleEntries.Should().ContainSingle(entry => entry.EntityType == "EnrichmentSourceSecret");

        var secretAudit = visibleEntries.Single(entry => entry.EntityType == "EnrichmentSourceSecret");
        secretAudit.TenantId.Should().Be(Guid.Empty);
        secretAudit.Action.Should().Be(AuditAction.Updated);
        secretAudit.UserId.Should().Be(_userId);
        secretAudit.OldValues.Should().Contain("system/enrichment-sources/nvd-old");
        secretAudit.NewValues.Should().Contain("system/enrichment-sources/nvd");
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
