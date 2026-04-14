using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class NormalizedSoftwareAlias
{
    public Guid Id { get; private set; }
    public Guid SoftwareProductId { get; private set; }
    public SoftwareIdentitySourceSystem SourceSystem { get; private set; }
    public string ExternalSoftwareId { get; private set; } = null!;
    public string RawName { get; private set; } = null!;
    public string? RawVendor { get; private set; }
    public string? RawVersion { get; private set; }
    public SoftwareNormalizationConfidence AliasConfidence { get; private set; }
    public string MatchReason { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public SoftwareProduct SoftwareProduct { get; private set; } = null!;

    private NormalizedSoftwareAlias() { }

    public static NormalizedSoftwareAlias Create(
        Guid softwareProductId,
        SoftwareIdentitySourceSystem sourceSystem,
        string externalSoftwareId,
        string rawName,
        string? rawVendor,
        string? rawVersion,
        SoftwareNormalizationConfidence aliasConfidence,
        string matchReason,
        DateTimeOffset timestamp
    )
    {
        return new NormalizedSoftwareAlias
        {
            Id = Guid.NewGuid(),
            SoftwareProductId = softwareProductId,
            SourceSystem = sourceSystem,
            ExternalSoftwareId = externalSoftwareId.Trim(),
            RawName = rawName.Trim(),
            RawVendor = string.IsNullOrWhiteSpace(rawVendor) ? null : rawVendor.Trim(),
            RawVersion = string.IsNullOrWhiteSpace(rawVersion) ? null : rawVersion.Trim(),
            AliasConfidence = aliasConfidence,
            MatchReason = matchReason.Trim(),
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
        };
    }

    public void UpdateMatch(
        Guid softwareProductId,
        string rawName,
        string? rawVendor,
        string? rawVersion,
        SoftwareNormalizationConfidence aliasConfidence,
        string matchReason,
        DateTimeOffset updatedAt
    )
    {
        SoftwareProductId = softwareProductId;
        RawName = rawName.Trim();
        RawVendor = string.IsNullOrWhiteSpace(rawVendor) ? null : rawVendor.Trim();
        RawVersion = string.IsNullOrWhiteSpace(rawVersion) ? null : rawVersion.Trim();
        AliasConfidence = aliasConfidence;
        MatchReason = matchReason.Trim();
        UpdatedAt = updatedAt;
    }
}
