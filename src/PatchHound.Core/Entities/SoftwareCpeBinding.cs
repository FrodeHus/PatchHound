using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class SoftwareCpeBinding
{
    public Guid Id { get; private set; }
    public Guid SoftwareProductId { get; private set; }
    public string Cpe23Uri { get; private set; } = null!;
    public CpeBindingMethod BindingMethod { get; private set; }
    public MatchConfidence Confidence { get; private set; }
    public string? MatchedVendor { get; private set; }
    public string? MatchedProduct { get; private set; }
    public string? MatchedVersion { get; private set; }
    public DateTimeOffset LastValidatedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public SoftwareProduct SoftwareProduct { get; private set; } = null!;

    private SoftwareCpeBinding() { }

    public static SoftwareCpeBinding Create(
        Guid softwareProductId,
        string cpe23Uri,
        CpeBindingMethod bindingMethod,
        MatchConfidence confidence,
        string? matchedVendor,
        string? matchedProduct,
        string? matchedVersion,
        DateTimeOffset validatedAt
    )
    {
        return new SoftwareCpeBinding
        {
            Id = Guid.NewGuid(),
            SoftwareProductId = softwareProductId,
            Cpe23Uri = cpe23Uri,
            BindingMethod = bindingMethod,
            Confidence = confidence,
            MatchedVendor = matchedVendor,
            MatchedProduct = matchedProduct,
            MatchedVersion = matchedVersion,
            LastValidatedAt = validatedAt,
            CreatedAt = validatedAt,
            UpdatedAt = validatedAt,
        };
    }

    public void Update(
        string cpe23Uri,
        CpeBindingMethod bindingMethod,
        MatchConfidence confidence,
        string? matchedVendor,
        string? matchedProduct,
        string? matchedVersion,
        DateTimeOffset validatedAt
    )
    {
        Cpe23Uri = cpe23Uri;
        BindingMethod = bindingMethod;
        Confidence = confidence;
        MatchedVendor = matchedVendor;
        MatchedProduct = matchedProduct;
        MatchedVersion = matchedVersion;
        LastValidatedAt = validatedAt;
        UpdatedAt = validatedAt;
    }
}
