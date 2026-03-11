using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models.Setup;
using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Secrets;
using PatchHound.Infrastructure.Tenants;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class SetupControllerTests : IDisposable
{
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly ISetupService _setupService;
    private readonly ISecretStore _secretStore;
    private readonly SetupController _controller;

    public SetupControllerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentUserId.Returns(Guid.NewGuid());
        _tenantContext.AccessibleTenantIds.Returns([]);

        var interceptor = new AuditSaveChangesInterceptor(_tenantContext);
        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(_tenantContext)
        );
        _setupService = Substitute.For<ISetupService>();
        _secretStore = Substitute.For<ISecretStore>();
        _controller = new SetupController(_setupService, _dbContext, _secretStore)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity(
                            [
                                new Claim("roles", "Tenant.Admin"),
                                new Claim("tid", "entra-tenant"),
                                new Claim("oid", "entra-object-id"),
                                new Claim("preferred_username", "admin@example.com"),
                                new Claim("name", "Admin User"),
                            ],
                            "TestAuth"
                        )
                    ),
                },
            },
        };
    }

    [Fact]
    public async Task Complete_WhenDefenderEnabled_ConfiguresDefaultSourceEvenWithoutTenantAccessFilter()
    {
        var tenant = Tenant.Create("Acme", "entra-tenant");
        var defaultSource = TenantSourceCatalog.CreateDefaultDefender(tenant.Id);
        await _dbContext.Tenants.AddAsync(tenant);
        await _dbContext.TenantSourceConfigurations.AddAsync(defaultSource);
        await _dbContext.SaveChangesAsync();

        _setupService
            .RequiresSetupForTenantAsync("entra-tenant", Arg.Any<CancellationToken>())
            .Returns(true);
        _setupService
            .CompleteSetupAsync(
                Arg.Any<SetupRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result<Tenant>.Success(tenant));

        var action = await _controller.Complete(
            new SetupCompleteRequest(
                "Acme",
                new DefenderSetupRequest(true, "client-id", "client-secret")
            ),
            CancellationToken.None
        );

        action.Should().BeOfType<OkResult>();

        var updatedSource = await _dbContext
            .TenantSourceConfigurations.IgnoreQueryFilters()
            .SingleAsync(item => item.Id == defaultSource.Id);

        updatedSource.Enabled.Should().BeTrue();
        updatedSource.CredentialTenantId.Should().Be("entra-tenant");
        updatedSource.ClientId.Should().Be("client-id");
        updatedSource.SecretRef.Should().Be(
            $"tenants/{tenant.Id}/sources/{TenantSourceCatalog.DefenderSourceKey}"
        );

        await _secretStore
            .Received(1)
            .PutSecretAsync(
                updatedSource.SecretRef,
                Arg.Is<IReadOnlyDictionary<string, string>>(values =>
                    values[TenantSourceCatalog.GetSecretKeyName(TenantSourceCatalog.DefenderSourceKey)]
                    == "client-secret"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
