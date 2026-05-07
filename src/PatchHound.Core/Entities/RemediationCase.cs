using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class RemediationCase
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SoftwareProductId { get; private set; }
    public RemediationCaseStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    public string? ThreatIntelSummary { get; private set; }
    public DateTimeOffset? ThreatIntelGeneratedAt { get; private set; }
    public string? ThreatIntelProfileName { get; private set; }

    public SoftwareProduct SoftwareProduct { get; private set; } = null!;

    private RemediationCase() { }

    public static RemediationCase Create(Guid tenantId, Guid softwareProductId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (softwareProductId == Guid.Empty)
            throw new ArgumentException("SoftwareProductId is required.", nameof(softwareProductId));

        var now = DateTimeOffset.UtcNow;
        return new RemediationCase
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SoftwareProductId = softwareProductId,
            Status = RemediationCaseStatus.Open,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Close()
    {
        if (Status == RemediationCaseStatus.Closed)
            return;
        Status = RemediationCaseStatus.Closed;
        ClosedAt = DateTimeOffset.UtcNow;
        UpdatedAt = ClosedAt.Value;
    }

    public void SetThreatIntel(string summary, string? profileName)
    {
        ThreatIntelSummary = summary;
        ThreatIntelGeneratedAt = DateTimeOffset.UtcNow;
        ThreatIntelProfileName = profileName;
        UpdatedAt = ThreatIntelGeneratedAt.Value;
    }

    public void Reopen()
    {
        if (Status == RemediationCaseStatus.Open)
            return;
        Status = RemediationCaseStatus.Open;
        ClosedAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
