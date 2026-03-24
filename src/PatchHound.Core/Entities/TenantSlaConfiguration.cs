namespace PatchHound.Core.Entities;

public class TenantSlaConfiguration
{
    public Guid TenantId { get; private set; }
    public int CriticalDays { get; private set; }
    public int HighDays { get; private set; }
    public int MediumDays { get; private set; }
    public int LowDays { get; private set; }
    public int ApprovalExpiryHours { get; private set; }

    private TenantSlaConfiguration() { }

    public static TenantSlaConfiguration CreateDefault(Guid tenantId)
    {
        return new TenantSlaConfiguration
        {
            TenantId = tenantId,
            CriticalDays = 7,
            HighDays = 30,
            MediumDays = 90,
            LowDays = 180,
            ApprovalExpiryHours = 24,
        };
    }

    public void Update(int criticalDays, int highDays, int mediumDays, int lowDays, int approvalExpiryHours = 24)
    {
        CriticalDays = criticalDays;
        HighDays = highDays;
        MediumDays = mediumDays;
        LowDays = lowDays;
        ApprovalExpiryHours = approvalExpiryHours;
    }
}
