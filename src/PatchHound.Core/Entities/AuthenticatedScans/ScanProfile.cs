namespace PatchHound.Core.Entities.AuthenticatedScans;

public class ScanProfile
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string CronSchedule { get; private set; } = string.Empty;
    public Guid ConnectionProfileId { get; private set; }
    public Guid ScanRunnerId { get; private set; }
    public bool Enabled { get; private set; }
    public DateTimeOffset? ManualRequestedAt { get; private set; }
    public DateTimeOffset? LastRunStartedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ScanProfile() { }

    public static ScanProfile Create(
        Guid tenantId,
        string name,
        string description,
        string cronSchedule,
        Guid connectionProfileId,
        Guid scanRunnerId,
        bool enabled)
    {
        var now = DateTimeOffset.UtcNow;
        return new ScanProfile
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name.Trim(),
            Description = description.Trim(),
            CronSchedule = cronSchedule.Trim(),
            ConnectionProfileId = connectionProfileId,
            ScanRunnerId = scanRunnerId,
            Enabled = enabled,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(string name, string description, string cronSchedule,
                       Guid connectionProfileId, Guid scanRunnerId, bool enabled)
    {
        Name = name.Trim();
        Description = description.Trim();
        CronSchedule = cronSchedule.Trim();
        ConnectionProfileId = connectionProfileId;
        ScanRunnerId = scanRunnerId;
        Enabled = enabled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RequestManualRun(DateTimeOffset at)
    {
        ManualRequestedAt = at;
        UpdatedAt = at;
    }

    public void ClearManualRequest()
    {
        ManualRequestedAt = null;
    }

    public void RecordRunStarted(DateTimeOffset at)
    {
        LastRunStartedAt = at;
    }
}
