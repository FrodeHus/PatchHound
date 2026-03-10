using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class RiskAcceptance
{
    public Guid Id { get; private set; }
    public Guid TenantVulnerabilityId { get; private set; }
    public Guid? AssetId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid RequestedBy { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public RiskAcceptanceStatus Status { get; private set; }
    public string Justification { get; private set; } = null!;
    public string? Conditions { get; private set; }
    public DateTimeOffset? ExpiryDate { get; private set; }
    public int? ReviewFrequency { get; private set; }
    public DateTimeOffset? NextReviewDate { get; private set; }

    private RiskAcceptance() { }

    public static RiskAcceptance Create(
        Guid tenantVulnerabilityId,
        Guid tenantId,
        Guid requestedBy,
        string justification,
        Guid? assetId = null,
        string? conditions = null,
        DateTimeOffset? expiryDate = null,
        int? reviewFrequency = null,
        DateTimeOffset? nextReviewDate = null
    )
    {
        return new RiskAcceptance
        {
            Id = Guid.NewGuid(),
            TenantVulnerabilityId = tenantVulnerabilityId,
            TenantId = tenantId,
            RequestedBy = requestedBy,
            RequestedAt = DateTimeOffset.UtcNow,
            Status = RiskAcceptanceStatus.Pending,
            Justification = justification,
            AssetId = assetId,
            Conditions = conditions,
            ExpiryDate = expiryDate,
            ReviewFrequency = reviewFrequency,
            NextReviewDate = nextReviewDate,
        };
    }

    public void Approve(
        Guid approvedBy,
        string? conditions = null,
        DateTimeOffset? expiryDate = null,
        int? reviewFrequency = null
    )
    {
        ApprovedBy = approvedBy;
        ApprovedAt = DateTimeOffset.UtcNow;
        Status = RiskAcceptanceStatus.Approved;

        if (conditions is not null)
            Conditions = conditions;
        if (expiryDate.HasValue)
            ExpiryDate = expiryDate;
        if (reviewFrequency.HasValue)
            ReviewFrequency = reviewFrequency;
    }

    public void Reject(Guid rejectedBy)
    {
        ApprovedBy = rejectedBy;
        ApprovedAt = DateTimeOffset.UtcNow;
        Status = RiskAcceptanceStatus.Rejected;
    }

    public void Expire()
    {
        Status = RiskAcceptanceStatus.Expired;
    }
}
