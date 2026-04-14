using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class SoftwareDescriptionJob
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SoftwareProductId { get; private set; }
    public Guid? TenantAiProfileId { get; private set; }
    public SoftwareDescriptionJobStatus Status { get; private set; }
    public string Error { get; private set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private SoftwareDescriptionJob() { }

    public static SoftwareDescriptionJob Create(
        Guid tenantId,
        Guid softwareProductId,
        Guid? tenantAiProfileId,
        DateTimeOffset requestedAt)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId is required.");
        if (softwareProductId == Guid.Empty) throw new ArgumentException("SoftwareProductId is required.");
        return new SoftwareDescriptionJob
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SoftwareProductId = softwareProductId,
            TenantAiProfileId = tenantAiProfileId,
            Status = SoftwareDescriptionJobStatus.Pending,
            RequestedAt = requestedAt,
            UpdatedAt = requestedAt,
        };
    }

    public void Start(DateTimeOffset startedAt)
    {
        Status = SoftwareDescriptionJobStatus.Running;
        StartedAt = startedAt;
        Error = string.Empty;
        UpdatedAt = startedAt;
    }

    public void CompleteSucceeded(DateTimeOffset completedAt)
    {
        Status = SoftwareDescriptionJobStatus.Succeeded;
        CompletedAt = completedAt;
        Error = string.Empty;
        UpdatedAt = completedAt;
    }

    public void CompleteFailed(DateTimeOffset completedAt, string error)
    {
        Status = SoftwareDescriptionJobStatus.Failed;
        CompletedAt = completedAt;
        Error = error;
        UpdatedAt = completedAt;
    }
}
