using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class NormalizedSoftwareResolver(PatchHoundDbContext dbContext)
{
    internal sealed record SoftwareIdentitySnapshot(
        Guid SoftwareAssetId,
        string ExternalSoftwareId,
        SoftwareIdentitySourceSystem SourceSystem,
        string CanonicalName,
        string? CanonicalVendor,
        string? Category,
        string CanonicalProductKey,
        string? PrimaryCpe23Uri,
        string? DetectedVersion,
        SoftwareNormalizationMethod NormalizationMethod,
        SoftwareNormalizationConfidence Confidence,
        string MatchReason
    );

    public sealed record ResolutionResult(
        Guid SoftwareProductId,
        Guid SoftwareAssetId,
        string? DetectedVersion,
        SoftwareIdentitySourceSystem SourceSystem
    );

    public Task<IReadOnlyDictionary<Guid, ResolutionResult>> SyncTenantAsync(
        Guid tenantId,
        CancellationToken ct
    )
    {
        // Phase 7c: software-type Asset records have been removed. Software identity
        // resolution is now handled by the canonical Device/InstalledSoftware pipeline
        // (StagedDeviceMergeService). Return an empty dictionary so the downstream
        // NormalizedSoftwareProjectionService call becomes a no-op.
        _ = dbContext;
        IReadOnlyDictionary<Guid, ResolutionResult> empty = new Dictionary<Guid, ResolutionResult>();
        return Task.FromResult(empty);
    }
}
