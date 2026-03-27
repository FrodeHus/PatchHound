using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class NormalizedSoftware
{
    public Guid Id { get; private set; }
    public string CanonicalName { get; private set; } = null!;
    public string? CanonicalVendor { get; private set; }
    public string CanonicalProductKey { get; private set; } = null!;
    public string? PrimaryCpe23Uri { get; private set; }
    public string? Description { get; private set; }
    public DateTimeOffset? DescriptionGeneratedAt { get; private set; }
    public string? DescriptionProviderType { get; private set; }
    public string? DescriptionProfileName { get; private set; }
    public string? DescriptionModel { get; private set; }
    public SoftwareNormalizationMethod NormalizationMethod { get; private set; }
    public SoftwareNormalizationConfidence Confidence { get; private set; }
    public DateTimeOffset LastEvaluatedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // End-of-life enrichment fields (from endoflife.date)
    public string? EolProductSlug { get; private set; }
    public DateTimeOffset? EolDate { get; private set; }
    public string? EolLatestVersion { get; private set; }
    public bool? EolIsLts { get; private set; }
    public DateTimeOffset? EolSupportEndDate { get; private set; }
    public bool? EolIsDiscontinued { get; private set; }
    public DateTimeOffset? EolEnrichedAt { get; private set; }

    private NormalizedSoftware() { }

    public static NormalizedSoftware Create(
        string canonicalName,
        string? canonicalVendor,
        string canonicalProductKey,
        string? primaryCpe23Uri,
        SoftwareNormalizationMethod normalizationMethod,
        SoftwareNormalizationConfidence confidence,
        DateTimeOffset timestamp
    )
    {
        return new NormalizedSoftware
        {
            Id = Guid.NewGuid(),
            CanonicalName = canonicalName.Trim(),
            CanonicalVendor = string.IsNullOrWhiteSpace(canonicalVendor) ? null : canonicalVendor.Trim(),
            CanonicalProductKey = canonicalProductKey.Trim(),
            PrimaryCpe23Uri = string.IsNullOrWhiteSpace(primaryCpe23Uri) ? null : primaryCpe23Uri.Trim(),
            NormalizationMethod = normalizationMethod,
            Confidence = confidence,
            LastEvaluatedAt = timestamp,
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
        };
    }

    public void UpdateIdentity(
        string canonicalName,
        string? canonicalVendor,
        string canonicalProductKey,
        string? primaryCpe23Uri,
        SoftwareNormalizationMethod normalizationMethod,
        SoftwareNormalizationConfidence confidence,
        DateTimeOffset evaluatedAt
    )
    {
        CanonicalName = canonicalName.Trim();
        CanonicalVendor = string.IsNullOrWhiteSpace(canonicalVendor) ? null : canonicalVendor.Trim();
        CanonicalProductKey = canonicalProductKey.Trim();
        PrimaryCpe23Uri = string.IsNullOrWhiteSpace(primaryCpe23Uri) ? null : primaryCpe23Uri.Trim();
        NormalizationMethod = normalizationMethod;
        Confidence = confidence;
        LastEvaluatedAt = evaluatedAt;
        UpdatedAt = evaluatedAt;
    }

    public void UpdateDescription(
        string description,
        DateTimeOffset generatedAt,
        string providerType,
        string profileName,
        string model
    )
    {
        Description = description.Trim();
        DescriptionGeneratedAt = generatedAt;
        DescriptionProviderType = providerType.Trim();
        DescriptionProfileName = profileName.Trim();
        DescriptionModel = model.Trim();
        UpdatedAt = generatedAt;
    }

    public void UpdateEndOfLife(
        string productSlug,
        DateTimeOffset? eolDate,
        string? latestVersion,
        bool? isLts,
        DateTimeOffset? supportEndDate,
        bool? isDiscontinued,
        DateTimeOffset enrichedAt
    )
    {
        EolProductSlug = productSlug.Trim().ToLowerInvariant();
        EolDate = eolDate;
        EolLatestVersion = latestVersion;
        EolIsLts = isLts;
        EolSupportEndDate = supportEndDate;
        EolIsDiscontinued = isDiscontinued;
        EolEnrichedAt = enrichedAt;
        UpdatedAt = enrichedAt;
    }
}
