using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models.Settings;
using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Secrets;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Tests.Api;

public class TenantAiProfilesControllerTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly ISecretStore _secretStore;
    private readonly ITenantAiConfigurationResolver _resolver;
    private readonly IAiReportProvider _provider;
    private readonly TenantAiProfilesController _controller;

    public TenantAiProfilesControllerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.AccessibleTenantIds.Returns([_tenantId]);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new PatchHoundDbContext(options, BuildServiceProvider(_tenantContext));
        _secretStore = Substitute.For<ISecretStore>();
        _resolver = Substitute.For<ITenantAiConfigurationResolver>();
        _provider = Substitute.For<IAiReportProvider>();
        _provider.ProviderType.Returns(TenantAiProviderType.OpenAi);

        _controller = new TenantAiProfilesController(
            _dbContext,
            _secretStore,
            _tenantContext,
            _resolver,
            [_provider]
        );
    }

    [Fact]
    public async Task Create_WithApiKey_PersistsProfileAndSecret()
    {
        var action = await _controller.Create(
            new SaveTenantAiProfileRequest(
                "Default",
                "OpenAi",
                true,
                true,
                "gpt-4.1-mini",
                "Prompt",
                0.2m,
                1.0m,
                1200,
                60,
                "https://api.openai.com/v1",
                "",
                "",
                "",
                "secret-value"
            ),
            CancellationToken.None
        );

        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<TenantAiProfileDto>().Subject;
        dto.IsDefault.Should().BeTrue();
        dto.HasSecret.Should().BeTrue();

        var profile = await _dbContext.TenantAiProfiles.SingleAsync();
        profile.Name.Should().Be("Default");
        profile.ProviderType.Should().Be(TenantAiProviderType.OpenAi);
        await _secretStore.Received(1).PutSecretAsync(
            profile.SecretRef,
            Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public async Task SetDefault_ClearsExistingDefault()
    {
        var first = TenantAiProfile.Create(
            _tenantId,
            "First",
            TenantAiProviderType.OpenAi,
            true,
            true,
            "gpt-4.1-mini",
            "Prompt",
            0.2m,
            null,
            1200,
            60
        );
        var second = TenantAiProfile.Create(
            _tenantId,
            "Second",
            TenantAiProviderType.OpenAi,
            false,
            true,
            "gpt-4.1-mini",
            "Prompt",
            0.2m,
            null,
            1200,
            60
        );

        await _dbContext.TenantAiProfiles.AddRangeAsync(first, second);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.SetDefault(second.Id, CancellationToken.None);

        action.Result.Should().BeOfType<OkObjectResult>();
        var profiles = await _dbContext.TenantAiProfiles.OrderBy(item => item.Name).ToListAsync();
        profiles.Single(item => item.Name == "First").IsDefault.Should().BeFalse();
        profiles.Single(item => item.Name == "Second").IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateProfile_PersistsValidationResult()
    {
        var profile = TenantAiProfile.Create(
            _tenantId,
            "Default",
            TenantAiProviderType.OpenAi,
            true,
            true,
            "gpt-4.1-mini",
            "Prompt",
            0.2m,
            null,
            1200,
            60,
            secretRef: "tenants/test/ai/default"
        );

        await _dbContext.TenantAiProfiles.AddAsync(profile);
        await _dbContext.SaveChangesAsync();

        _resolver
            .ResolveByIdAsync(_tenantId, profile.Id, Arg.Any<CancellationToken>())
            .Returns(Result<TenantAiProfileResolved>.Success(new TenantAiProfileResolved(profile, "secret")));
        _provider
            .ValidateAsync(Arg.Any<TenantAiProfileResolved>(), Arg.Any<CancellationToken>())
            .Returns(AiProviderValidationResult.Success());

        var action = await _controller.ValidateProfile(profile.Id, CancellationToken.None);

        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<TenantAiProfileValidationResultDto>().Subject;
        dto.ValidationStatus.Should().Be("Valid");

        var updated = await _dbContext.TenantAiProfiles.SingleAsync();
        updated.LastValidationStatus.Should().Be(TenantAiProfileValidationStatus.Valid);
        updated.LastValidatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Create_DefaultDisabledProfile_ReturnsBadRequest()
    {
        var action = await _controller.Create(
            new SaveTenantAiProfileRequest(
                "Default",
                "OpenAi",
                true,
                false,
                "gpt-4.1-mini",
                "Prompt",
                0.2m,
                1.0m,
                1200,
                60,
                "https://api.openai.com/v1",
                "",
                "",
                "",
                "secret-value"
            ),
            CancellationToken.None
        );

        action.Result.Should().BeOfType<BadRequestObjectResult>();
        (await _dbContext.TenantAiProfiles.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Update_ProfileConfiguration_ResetsValidationStatus()
    {
        var profile = TenantAiProfile.Create(
            _tenantId,
            "Default",
            TenantAiProviderType.OpenAi,
            true,
            true,
            "gpt-4.1-mini",
            "Prompt",
            0.2m,
            1.0m,
            1200,
            60,
            "https://api.openai.com/v1",
            secretRef: "tenants/test/ai/default"
        );
        profile.RecordValidation(TenantAiProfileValidationStatus.Valid, string.Empty);

        await _dbContext.TenantAiProfiles.AddAsync(profile);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.Update(
            profile.Id,
            new SaveTenantAiProfileRequest(
                "Default",
                "OpenAi",
                true,
                true,
                "gpt-4.1",
                "Updated prompt",
                0.3m,
                0.9m,
                1500,
                45,
                "https://api.openai.com/v1",
                "",
                "",
                "",
                ""
            ),
            CancellationToken.None
        );

        action.Result.Should().BeOfType<OkObjectResult>();

        var updated = await _dbContext.TenantAiProfiles.SingleAsync();
        updated.LastValidationStatus.Should().Be(TenantAiProfileValidationStatus.Unknown);
        updated.LastValidatedAt.Should().BeNull();
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
