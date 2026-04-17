using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class StagedCloudApplicationMergeService(
    PatchHoundDbContext db,
    ILogger<StagedCloudApplicationMergeService> logger
) : IStagedCloudApplicationMergeService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<StagedCloudApplicationMergeSummary> MergeAsync(
        Guid ingestionRunId,
        Guid tenantId,
        CancellationToken ct
    )
    {
        var staged = await db.StagedCloudApplications
            .IgnoreQueryFilters()
            .Where(s => s.IngestionRunId == ingestionRunId && s.TenantId == tenantId)
            .ToListAsync(ct);

        if (staged.Count == 0)
            return new StagedCloudApplicationMergeSummary(0, 0, 0);

        var sourceSystems = await db.SourceSystems
            .ToDictionaryAsync(s => s.Key, StringComparer.Ordinal, ct);

        var seenExternalIds = new HashSet<string>(StringComparer.Ordinal);
        var applicationsCreated = 0;
        var applicationsTouched = 0;

        foreach (var stagedApp in staged)
        {
            var normalizedKey = stagedApp.SourceKey.Trim().ToLowerInvariant();
            if (!sourceSystems.TryGetValue(normalizedKey, out var sourceSystem))
            {
                throw new InvalidOperationException(
                    $"Unknown source system key '{stagedApp.SourceKey}'. Seed it before ingesting."
                );
            }

            var payload = JsonSerializer.Deserialize<IngestionCloudApplication>(
                stagedApp.PayloadJson,
                SerializerOptions
            );
            if (payload is null)
            {
                logger.LogWarning(
                    "StagedCloudApplication {Id} has null payload, skipping.",
                    stagedApp.Id
                );
                continue;
            }

            seenExternalIds.Add(stagedApp.ExternalId);

            var existing = await db.CloudApplications
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    a =>
                        a.TenantId == tenantId
                        && a.SourceSystemId == sourceSystem.Id
                        && a.ExternalId == stagedApp.ExternalId,
                    ct
                );

            if (existing is null)
            {
                existing = CloudApplication.Create(
                    tenantId,
                    sourceSystem.Id,
                    stagedApp.ExternalId,
                    stagedApp.Name,
                    stagedApp.Description
                );
                db.CloudApplications.Add(existing);
                await db.SaveChangesAsync(ct);
                applicationsCreated++;
            }
            else
            {
                existing.Update(stagedApp.Name, stagedApp.Description);
                existing.SetActiveInTenant(true);
                applicationsTouched++;
            }

            // Replace credentials wholesale — Graph always returns the full set.
            var oldCredentials = await db.CloudApplicationCredentialMetadata
                .Where(c => c.CloudApplicationId == existing.Id)
                .ToListAsync(ct);
            db.CloudApplicationCredentialMetadata.RemoveRange(oldCredentials);

            foreach (var cred in payload.Credentials)
            {
                db.CloudApplicationCredentialMetadata.Add(
                    CloudApplicationCredentialMetadata.Create(
                        cloudApplicationId: existing.Id,
                        tenantId: tenantId,
                        externalId: cred.ExternalId,
                        type: cred.Type,
                        displayName: cred.DisplayName,
                        expiresAt: cred.ExpiresAt
                    )
                );
            }
        }

        await db.SaveChangesAsync(ct);

        // Deactivate apps not present in this run.
        var applicationsDeactivated = await db.CloudApplications
            .IgnoreQueryFilters()
            .Where(a =>
                a.TenantId == tenantId
                && a.ActiveInTenant
                && !seenExternalIds.Contains(a.ExternalId)
            )
            .ExecuteUpdateAsync(
                s => s.SetProperty(a => a.ActiveInTenant, false)
                    .SetProperty(a => a.UpdatedAt, DateTimeOffset.UtcNow),
                ct
            );

        return new StagedCloudApplicationMergeSummary(
            applicationsCreated,
            applicationsTouched,
            applicationsDeactivated
        );
    }
}
