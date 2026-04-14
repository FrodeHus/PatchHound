using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class RiskAcceptance
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid RemediationCaseId { get; private set; }
    public Guid? VulnerabilityId { get; private set; }
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

    public RemediationCase RemediationCase { get; private set; } = null!;

    private RiskAcceptance() { }

    public static RiskAcceptance Create(
        Guid tenantId,
        Guid remediationCaseId,
        Guid requestedBy,
        string justification,
        Guid? vulnerabilityId = null,
        string? conditions = null,
        DateTimeOffset? expiryDate = null,
        int? reviewFrequency = null,
        DateTimeOffset? nextReviewDate = null)
    {
        if (string.IsNullOrWhiteSpace(justification))
            throw new ArgumentException("Justification is required.", nameof(justification));

        return new RiskAcceptance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RemediationCaseId = remediationCaseId,
            VulnerabilityId = vulnerabilityId,
            RequestedBy = requestedBy,
            RequestedAt = DateTimeOffset.UtcNow,
            Status = RiskAcceptanceStatus.Pending,
            Justification = justification,
            Conditions = conditions,
            ExpiryDate = expiryDate,
            ReviewFrequency = reviewFrequency,
            NextReviewDate = nextReviewDate,
        };
    }

    public void Approve(Guid approvedBy, string? conditions = null, DateTimeOffset? expiryDate = null, int? reviewFrequency = null)
    {
        ApprovedBy = approvedBy;
        ApprovedAt = DateTimeOffset.UtcNow;
        Status = RiskAcceptanceStatus.Approved;
        if (conditions is not null) Conditions = conditions;
        if (expiryDate.HasValue) ExpiryDate = expiryDate;
        if (reviewFrequency.HasValue) ReviewFrequency = reviewFrequency;
    }

    public void Reject(Guid rejectedBy)
    {
        ApprovedBy = rejectedBy;
        ApprovedAt = DateTimeOffset.UtcNow;
        Status = RiskAcceptanceStatus.Rejected;
    }

    public void Expire() => Status = RiskAcceptanceStatus.Expired;
}
