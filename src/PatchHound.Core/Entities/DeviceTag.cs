namespace PatchHound.Core.Entities;

public class DeviceTag
{
    public const int KeyMaxLength = 128;
    public const int ValueMaxLength = 256;

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid DeviceId { get; private set; }
    public string Key { get; private set; } = null!;
    public string Value { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    private DeviceTag() { }

    public static DeviceTag Create(Guid tenantId, Guid deviceId, string key, string value)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        }
        if (deviceId == Guid.Empty)
        {
            throw new ArgumentException("DeviceId is required.", nameof(deviceId));
        }
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key is required.", nameof(key));
        }
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", nameof(value));
        }

        var normalizedKey = key.Trim();
        var normalizedValue = value.Trim();

        if (normalizedKey.Length > KeyMaxLength)
        {
            throw new ArgumentException(
                $"Key must be {KeyMaxLength} characters or fewer.",
                nameof(key));
        }
        if (normalizedValue.Length > ValueMaxLength)
        {
            throw new ArgumentException(
                $"Value must be {ValueMaxLength} characters or fewer.",
                nameof(value));
        }

        return new DeviceTag
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DeviceId = deviceId,
            Key = normalizedKey,
            Value = normalizedValue,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
