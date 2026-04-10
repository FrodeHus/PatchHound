namespace PatchHound.Core.Entities;

public class DeviceBusinessLabel
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid DeviceId { get; private set; }
    public Guid BusinessLabelId { get; private set; }

    public BusinessLabel BusinessLabel { get; private set; } = null!;

    private DeviceBusinessLabel() { }

    public static DeviceBusinessLabel Create(Guid tenantId, Guid deviceId, Guid businessLabelId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        }
        if (deviceId == Guid.Empty)
        {
            throw new ArgumentException("DeviceId is required.", nameof(deviceId));
        }
        if (businessLabelId == Guid.Empty)
        {
            throw new ArgumentException("BusinessLabelId is required.", nameof(businessLabelId));
        }

        return new DeviceBusinessLabel
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DeviceId = deviceId,
            BusinessLabelId = businessLabelId,
        };
    }
}
