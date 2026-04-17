using Microsoft.Extensions.Logging;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Infrastructure.CredentialSources;

public class EntraApplicationSource(
    EntraGraphApiClient apiClient,
    EntraApplicationsConfigurationProvider configurationProvider,
    ILogger<EntraApplicationSource> logger
) : ICloudApplicationSource
{
    public string SourceKey => TenantSourceCatalog.EntraApplicationsSourceKey;
    public string SourceName => "EntraApplications";

    public async Task<IngestionCloudApplicationSnapshot> FetchCloudApplicationsAsync(
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
            return new IngestionCloudApplicationSnapshot([]);
        }

        logger.LogInformation("Fetching Entra applications for tenant {TenantId}", tenantId);

        var applications = await apiClient.GetApplicationsAsync(configuration, ct);

        logger.LogInformation(
            "Fetched {Count} Entra applications for tenant {TenantId}",
            applications.Count,
            tenantId
        );

        var ingestionApps = applications
            .Where(app => !string.IsNullOrWhiteSpace(app.Id))
            .Select(MapToIngestionCloudApplication)
            .ToList();

        return new IngestionCloudApplicationSnapshot(ingestionApps);
    }

    private static IngestionCloudApplication MapToIngestionCloudApplication(GraphApplication app)
    {
        var credentials = new List<IngestionCloudApplicationCredential>();

        foreach (var secret in app.PasswordCredentials.Where(s => s.EndDateTime.HasValue))
        {
            credentials.Add(new IngestionCloudApplicationCredential(
                ExternalId: secret.KeyId ?? Guid.NewGuid().ToString(),
                Type: "Secret",
                DisplayName: secret.DisplayName,
                ExpiresAt: secret.EndDateTime!.Value
            ));
        }

        foreach (var cert in app.KeyCredentials.Where(c => c.EndDateTime.HasValue))
        {
            credentials.Add(new IngestionCloudApplicationCredential(
                ExternalId: cert.KeyId ?? Guid.NewGuid().ToString(),
                Type: cert.Type ?? "Certificate",
                DisplayName: cert.DisplayName,
                ExpiresAt: cert.EndDateTime!.Value
            ));
        }

        return new IngestionCloudApplication(
            ExternalId: app.Id!,
            Name: app.DisplayName ?? app.Id!,
            Description: app.Description,
            Credentials: credentials
        );
    }
}
