using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models.Admin;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Secrets;
using PatchHound.Infrastructure.Services;

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

        _dbContext = new PatchHoundDbContext(options, BuildServiceProvider(_tenantContext));
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
