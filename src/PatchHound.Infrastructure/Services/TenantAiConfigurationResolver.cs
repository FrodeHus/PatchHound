using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Secrets;

namespace PatchHound.Infrastructure.Services;

public class TenantAiConfigurationResolver : ITenantAiConfigurationResolver
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly ISecretStore _secretStore;

    public TenantAiConfigurationResolver(PatchHoundDbContext dbContext, ISecretStore secretStore)
    {
        _dbContext = dbContext;
        _secretStore = secretStore;
    }

    public async Task<Result<TenantAiProfileResolved>> ResolveDefaultAsync(
        Guid tenantId,
        CancellationToken ct
    )
    {
        var profile = await _dbContext
            .TenantAiProfiles.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.IsDefault && item.IsEnabled)
            .OrderBy(item => item.Name)
            .FirstOrDefaultAsync(ct);

        if (profile is null)
        {
            return Result<TenantAiProfileResolved>.Failure(
                "No enabled default AI profile is configured for this tenant."
            );
        }

        return await ResolveAsync(profile, ct);
    }

    public async Task<Result<TenantAiProfileResolved>> ResolveByIdAsync(
        Guid tenantId,
        Guid profileId,
        CancellationToken ct
    )
    {
        var profile = await _dbContext
            .TenantAiProfiles.AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == profileId && item.TenantId == tenantId && item.IsEnabled,
                ct
            );

        if (profile is null)
        {
            return Result<TenantAiProfileResolved>.Failure("AI profile not found for this tenant.");
        }

        return await ResolveAsync(profile, ct);
    }

    private async Task<Result<TenantAiProfileResolved>> ResolveAsync(
        TenantAiProfile profile,
        CancellationToken ct
    )
    {
        var apiKey = string.Empty;
        if (!string.IsNullOrWhiteSpace(profile.SecretRef))
        {
            apiKey = await _secretStore.GetSecretAsync(profile.SecretRef, "apiKey", ct) ?? string.Empty;
        }

        return Result<TenantAiProfileResolved>.Success(new TenantAiProfileResolved(profile, apiKey));
    }
}
