using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class SoftwareProduct
{
    public Guid Id { get; private set; }
    public string CanonicalProductKey { get; private set; } = null!;
    public string Vendor { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? PrimaryCpe23Uri { get; private set; }
    public DateTimeOffset? EndOfLifeAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Normalization identity fields (migrated from NormalizedSoftware)
    public string? Category { get; private set; }
    public SoftwareNormalizationMethod NormalizationMethod { get; private set; }
    public SoftwareNormalizationConfidence Confidence { get; private set; }
    public DateTimeOffset LastEvaluatedAt { get; private set; }

    // Description enrichment fields
    public string? Description { get; private set; }
    public DateTimeOffset? DescriptionGeneratedAt { get; private set; }
    public string? DescriptionProviderType { get; private set; }
    public string? DescriptionProfileName { get; private set; }
    public string? DescriptionModel { get; private set; }

    // End-of-life enrichment fields (from endoflife.date)
    public string? EolProductSlug { get; private set; }
    public DateTimeOffset? EolDate { get; private set; }
    public string? EolLatestVersion { get; private set; }
    public bool? EolIsLts { get; private set; }
    public DateTimeOffset? EolSupportEndDate { get; private set; }
    public bool? EolIsDiscontinued { get; private set; }
    public DateTimeOffset? EolEnrichedAt { get; private set; }

    // Supply-chain enrichment fields
    public SupplyChainRemediationPath SupplyChainRemediationPath { get; private set; }
    public SupplyChainInsightConfidence SupplyChainInsightConfidence { get; private set; }
    public string? SupplyChainSourceFormat { get; private set; }
    public string? SupplyChainPrimaryComponentName { get; private set; }
    public string? SupplyChainPrimaryComponentVersion { get; private set; }
    public string? SupplyChainFixedVersion { get; private set; }
    public int? SupplyChainAffectedVulnerabilityCount { get; private set; }
    public string? SupplyChainSummary { get; private set; }
    public DateTimeOffset? SupplyChainEnrichedAt { get; private set; }

    private SoftwareProduct() { }

    public static SoftwareProduct Create(string vendor, string name, string? primaryCpe23Uri)
    {
        if (string.IsNullOrWhiteSpace(vendor))
        {
            throw new ArgumentException("Vendor is required.", nameof(vendor));
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        var normalizedVendor = vendor.Trim();
        var normalizedName = name.Trim();

        if (normalizedVendor.Length > 256)
        {
            throw new ArgumentException("Vendor must be 256 characters or fewer.", nameof(vendor));
        }
        if (normalizedName.Length > 512)
        {
            throw new ArgumentException("Name must be 512 characters or fewer.", nameof(name));
        }

        var canonicalProductKey = $"{normalizedVendor.ToLowerInvariant()}::{normalizedName.ToLowerInvariant()}";
        if (canonicalProductKey.Length > 512)
        {
            throw new ArgumentException("Combined vendor and name exceed the 512-character canonical key limit.", nameof(name));
        }

        if (primaryCpe23Uri is not null && primaryCpe23Uri.Length > 512)
        {
            throw new ArgumentException("PrimaryCpe23Uri must be 512 characters or fewer.", nameof(primaryCpe23Uri));
        }

        var now = DateTimeOffset.UtcNow;
        return new SoftwareProduct
        {
            Id = Guid.NewGuid(),
            Vendor = normalizedVendor,
            Name = normalizedName,
            CanonicalProductKey = canonicalProductKey,
            PrimaryCpe23Uri = primaryCpe23Uri,
            NormalizationMethod = SoftwareNormalizationMethod.Heuristic,
            Confidence = SoftwareNormalizationConfidence.Medium,
            LastEvaluatedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void UpdatePrimaryCpe(string? cpe)
    {
        PrimaryCpe23Uri = cpe;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetEndOfLife(DateTimeOffset? at)
    {
        EndOfLifeAt = at;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateIdentity(
        string? category,
        string? primaryCpe23Uri,
        SoftwareNormalizationMethod normalizationMethod,
        SoftwareNormalizationConfidence confidence,
        DateTimeOffset evaluatedAt
    )
    {
        Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        PrimaryCpe23Uri = string.IsNullOrWhiteSpace(primaryCpe23Uri) ? PrimaryCpe23Uri : primaryCpe23Uri.Trim();
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

    public void UpdateSupplyChainInsight(
        SupplyChainRemediationPath remediationPath,
        SupplyChainInsightConfidence confidence,
        string sourceFormat,
        string? primaryComponentName,
        string? primaryComponentVersion,
        string? fixedVersion,
        int? affectedVulnerabilityCount,
        string summary,
        DateTimeOffset enrichedAt
    )
    {
        SupplyChainRemediationPath = remediationPath;
        SupplyChainInsightConfidence = confidence;
        SupplyChainSourceFormat = sourceFormat.Trim();
        SupplyChainPrimaryComponentName = string.IsNullOrWhiteSpace(primaryComponentName)
            ? null
            : primaryComponentName.Trim();
        SupplyChainPrimaryComponentVersion = string.IsNullOrWhiteSpace(primaryComponentVersion)
            ? null
            : primaryComponentVersion.Trim();
        SupplyChainFixedVersion = string.IsNullOrWhiteSpace(fixedVersion) ? null : fixedVersion.Trim();
        SupplyChainAffectedVulnerabilityCount = affectedVulnerabilityCount;
        SupplyChainSummary = summary.Trim();
        SupplyChainEnrichedAt = enrichedAt;
        UpdatedAt = enrichedAt;
    }

    public void ClearSupplyChainInsight()
    {
        SupplyChainRemediationPath = SupplyChainRemediationPath.Unknown;
        SupplyChainInsightConfidence = SupplyChainInsightConfidence.Unknown;
        SupplyChainSourceFormat = null;
        SupplyChainPrimaryComponentName = null;
        SupplyChainPrimaryComponentVersion = null;
        SupplyChainFixedVersion = null;
        SupplyChainAffectedVulnerabilityCount = null;
        SupplyChainSummary = null;
        SupplyChainEnrichedAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
