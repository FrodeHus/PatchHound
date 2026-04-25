using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models.Integrations;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Credentials;
using PatchHound.Infrastructure.Data;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class IntegrationsControllerTests : IDisposable
{
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly IntegrationsController _controller;

    public IntegrationsControllerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(_tenantContext)
        );
        _controller = new IntegrationsController(_dbContext);
    }

    [Fact]
    public async Task UpdateSentinelConnector_WhenEnabledWithoutCredential_ReturnsValidationProblem()
    {
        var result = await _controller.UpdateSentinelConnector(
            new UpdateSentinelConnectorRequest(
                Enabled: true,
                DceEndpoint: "https://example.ingest.monitor.azure.com",
                DcrImmutableId: "dcr-123",
                StreamName: "Custom-PatchHoundAuditLog",
                StoredCredentialId: null
            ),
            CancellationToken.None
        );

        result.Should().BeOfType<ObjectResult>();
        (await _dbContext.SentinelConnectorConfigurations.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UpdateSentinelConnector_WhenCredentialIsNotGlobal_ReturnsValidationProblem()
    {
        var credential = StoredCredential.Create(
            "Tenant credential",
            StoredCredentialTypes.EntraClientSecret,
            isGlobal: false,
            credentialTenantId: "tenant",
            clientId: "client",
            secretRef: "secret/ref",
            now: DateTimeOffset.UtcNow
        );
        await _dbContext.StoredCredentials.AddAsync(credential);
        await _dbContext.SaveChangesAsync();

        var result = await _controller.UpdateSentinelConnector(
            new UpdateSentinelConnectorRequest(
                Enabled: true,
                DceEndpoint: "https://example.ingest.monitor.azure.com",
                DcrImmutableId: "dcr-123",
                StreamName: "Custom-PatchHoundAuditLog",
                StoredCredentialId: credential.Id
            ),
            CancellationToken.None
        );

        result.Should().BeOfType<ObjectResult>();
        (await _dbContext.SentinelConnectorConfigurations.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UpdateSentinelConnector_WhenGlobalCredentialProvided_SavesReference()
    {
        var credential = StoredCredential.Create(
            "Global credential",
            StoredCredentialTypes.EntraClientSecret,
            isGlobal: true,
            credentialTenantId: "tenant",
            clientId: "client",
            secretRef: "secret/ref",
            now: DateTimeOffset.UtcNow
        );
        await _dbContext.StoredCredentials.AddAsync(credential);
        await _dbContext.SaveChangesAsync();

        var result = await _controller.UpdateSentinelConnector(
            new UpdateSentinelConnectorRequest(
                Enabled: true,
                DceEndpoint: " https://example.ingest.monitor.azure.com ",
                DcrImmutableId: " dcr-123 ",
                StreamName: " Custom-PatchHoundAuditLog ",
                StoredCredentialId: credential.Id
            ),
            CancellationToken.None
        );

        result.Should().BeOfType<NoContentResult>();

        var saved = await _dbContext.SentinelConnectorConfigurations.SingleAsync();
        saved.Enabled.Should().BeTrue();
        saved.StoredCredentialId.Should().Be(credential.Id);
        saved.DceEndpoint.Should().Be("https://example.ingest.monitor.azure.com");
        saved.DcrImmutableId.Should().Be("dcr-123");
        saved.StreamName.Should().Be("Custom-PatchHoundAuditLog");
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
