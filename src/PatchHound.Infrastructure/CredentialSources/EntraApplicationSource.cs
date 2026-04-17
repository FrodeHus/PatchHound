using System.Text.Json;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Infrastructure.CredentialSources;

public class EntraApplicationSource(
    EntraGraphApiClient apiClient,
    EntraApplicationsConfigurationProvider configurationProvider,
    ILogger<EntraApplicationSource> logger
) : IVulnerabilitySource, IAssetInventorySource
{
    public string SourceKey => TenantSourceCatalog.EntraApplicationsSourceKey;
    public string SourceName => "EntraApplications";

    public Task<IReadOnlyList<IngestionResult>> FetchVulnerabilitiesAsync(
        Guid tenantId,
        CancellationToken ct
    ) => Task.FromResult<IReadOnlyList<IngestionResult>>([]);

    public Task<CanonicalVulnerabilityBatch> FetchCanonicalVulnerabilitiesAsync(
        Guid tenantId,
        CancellationToken ct
    ) => Task.FromResult(new CanonicalVulnerabilityBatch([], []));

    public async Task<IngestionAssetInventorySnapshot> FetchAssetsAsync(
        Guid tenantId,
        CancellationToken ct
    )
    {
        var configuration = await configurationProvider.GetConfigurationAsync(tenantId, ct);
        if (configuration is null)
        {
            logger.LogInformation(
                "Skipping Entra Applications ingestion for tenant {TenantId}: no enabled credentials configured.",
                tenantId
            );
            return new IngestionAssetInventorySnapshot([], []);
        }

        logger.LogInformation(
            "Fetching Entra applications for tenant {TenantId}",
            tenantId
        );

        var applications = await apiClient.GetApplicationsAsync(configuration, ct);

        logger.LogInformation(
            "Fetched {Count} Entra applications for tenant {TenantId}",
            applications.Count,
            tenantId
        );

        var assets = applications
            .Where(app => !string.IsNullOrWhiteSpace(app.Id))
            .Select(app => MapToIngestionAsset(app))
            .ToList();

        return new IngestionAssetInventorySnapshot(assets, []);
    }

    private static IngestionAsset MapToIngestionAsset(GraphApplication app)
    {
        var credentials = new List<object>();

        foreach (var secret in app.PasswordCredentials.Where(s => s.EndDateTime.HasValue))
        {
            credentials.Add(new
            {
                externalId = secret.KeyId,
                type = "Secret",
                displayName = secret.DisplayName,
                expiresAt = secret.EndDateTime!.Value,
            });
        }

        foreach (var cert in app.KeyCredentials.Where(c => c.EndDateTime.HasValue))
        {
            credentials.Add(new
            {
                externalId = cert.KeyId,
                type = cert.Type ?? "Certificate",
                displayName = cert.DisplayName,
                expiresAt = cert.EndDateTime!.Value,
            });
        }

        var metadata = JsonSerializer.Serialize(new { credentials });

        return new IngestionAsset(
            ExternalId: app.Id,
            Name: app.DisplayName ?? app.Id,
            AssetType: AssetType.Application,
            Description: app.Description,
            Metadata: metadata
        );
    }
}
