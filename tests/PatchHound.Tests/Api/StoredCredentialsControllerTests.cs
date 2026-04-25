using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models.Credentials;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Credentials;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Secrets;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class StoredCredentialsControllerTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly ISecretStore _secretStore;
    private readonly StoredCredentialsController _controller;

    public StoredCredentialsControllerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.AccessibleTenantIds.Returns([_tenantId]);
        _tenantContext.HasAccessToTenant(_tenantId).Returns(true);
        _tenantContext.HasAccessToTenant(Arg.Is<Guid>(id => id != _tenantId)).Returns(false);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
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
        _secretStore
            .DeleteSecretPathAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        _controller = new StoredCredentialsController(
            _dbContext,
            _secretStore,
            _tenantContext,
            new AuditLogWriter(_dbContext, _tenantContext)
        );
    }

    [Fact]
    public async Task Create_WhenTenantScoped_PersistsCredentialScopeAndSecret()
    {
        var result = await _controller.Create(
            new CreateStoredCredentialRequest(
                "Defender",
                StoredCredentialTypes.EntraClientSecret,
                IsGlobal: false,
                CredentialTenantId: "entra-tenant",
                ClientId: "client-id",
                ClientSecret: " client-secret ",
                TenantIds: [_tenantId]
            ),
            CancellationToken.None
        );

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var dto = created.Value.Should().BeOfType<StoredCredentialDto>().Subject;
        dto.Name.Should().Be("Defender");
        dto.IsGlobal.Should().BeFalse();
        dto.TenantIds.Should().ContainSingle().Which.Should().Be(_tenantId);

        var saved = await _dbContext.StoredCredentials
            .Include(credential => credential.TenantScopes)
            .SingleAsync();
        saved.SecretRef.Should().Be($"stored-credentials/{saved.Id}");
        saved.TenantScopes.Should().ContainSingle(scope => scope.TenantId == _tenantId);
        await _secretStore.Received(1).PutSecretAsync(
            saved.SecretRef,
            Arg.Is<IReadOnlyDictionary<string, string>>(values =>
                values[StoredCredentialSecretKeys.ClientSecret] == "client-secret"),
            Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public async Task Create_WhenApiKey_PersistsApiKeySecretWithoutEntraFields()
    {
        var result = await _controller.Create(
            new CreateStoredCredentialRequest(
                "NVD",
                StoredCredentialTypes.ApiKey,
                IsGlobal: true,
                CredentialTenantId: string.Empty,
                ClientId: string.Empty,
                ClientSecret: " nvd-api-key ",
                TenantIds: []
            ),
            CancellationToken.None
        );

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var dto = created.Value.Should().BeOfType<StoredCredentialDto>().Subject;
        dto.Type.Should().Be(StoredCredentialTypes.ApiKey);
        dto.TypeDisplayName.Should().Be("API key");
        dto.CredentialTenantId.Should().BeEmpty();
        dto.ClientId.Should().BeEmpty();

        var saved = await _dbContext.StoredCredentials.SingleAsync();
        await _secretStore.Received(1).PutSecretAsync(
            saved.SecretRef,
            Arg.Is<IReadOnlyDictionary<string, string>>(values =>
                values[StoredCredentialSecretKeys.ApiKey] == "nvd-api-key"),
            Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public async Task List_WhenTenantFilterProvided_ReturnsGlobalAndScopedCredentials()
    {
        var otherTenantId = Guid.NewGuid();
        var global = StoredCredential.Create(
            "Global",
            StoredCredentialTypes.EntraClientSecret,
            true,
            "entra",
            "client",
            "secret/global",
            DateTimeOffset.UtcNow
        );
        var scoped = StoredCredential.Create(
            "Scoped",
            StoredCredentialTypes.EntraClientSecret,
            false,
            "entra",
            "client",
            "secret/scoped",
            DateTimeOffset.UtcNow
        );
        scoped.TenantScopes.Add(StoredCredentialTenant.Create(scoped.Id, _tenantId));
        var inaccessible = StoredCredential.Create(
            "Other",
            StoredCredentialTypes.EntraClientSecret,
            false,
            "entra",
            "client",
            "secret/other",
            DateTimeOffset.UtcNow
        );
        inaccessible.TenantScopes.Add(StoredCredentialTenant.Create(inaccessible.Id, otherTenantId));

        await _dbContext.StoredCredentials.AddRangeAsync(global, scoped, inaccessible);
        await _dbContext.SaveChangesAsync();

        var result = await _controller.List(
            StoredCredentialTypes.EntraClientSecret,
            _tenantId,
            CancellationToken.None
        );

        var credentials = result.Value.Should().NotBeNull().And.Subject;
        credentials.Select(credential => credential.Name).Should().BeEquivalentTo(["Global", "Scoped"]);
    }

    [Fact]
    public async Task Create_WhenSecretWriteFails_DoesNotPersistCredential()
    {
        _secretStore
            .PutSecretAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => throw new InvalidOperationException("secret store unavailable"));

        var act = () => _controller.Create(
            new CreateStoredCredentialRequest(
                "Defender",
                StoredCredentialTypes.EntraClientSecret,
                IsGlobal: false,
                CredentialTenantId: "entra-tenant",
                ClientId: "client-id",
                ClientSecret: "client-secret",
                TenantIds: [_tenantId]
            ),
            CancellationToken.None
        );

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await _dbContext.StoredCredentials.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Delete_WhenCredentialUsedBySentinel_ReturnsConflict()
    {
        var credential = StoredCredential.Create(
            "Sentinel",
            StoredCredentialTypes.EntraClientSecret,
            true,
            "entra",
            "client",
            "secret/sentinel",
            DateTimeOffset.UtcNow
        );
        var sentinel = SentinelConnectorConfiguration.Create(
            enabled: true,
            dceEndpoint: "https://example.ingest.monitor.azure.com",
            dcrImmutableId: "dcr-123",
            streamName: "Custom-PatchHoundAuditLog",
            storedCredentialId: credential.Id
        );

        await _dbContext.StoredCredentials.AddAsync(credential);
        await _dbContext.SentinelConnectorConfigurations.AddAsync(sentinel);
        await _dbContext.SaveChangesAsync();

        var result = await _controller.Delete(credential.Id, CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>();
        await _secretStore.DidNotReceiveWithAnyArgs().DeleteSecretPathAsync(default!, default);
        (await _dbContext.StoredCredentials.CountAsync()).Should().Be(1);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
