namespace PatchHound.Core.Entities;

public class SentinelConnectorConfiguration
{
    public Guid Id { get; private set; }
    public bool Enabled { get; private set; }
    public string DceEndpoint { get; private set; } = string.Empty;
    public string DcrImmutableId { get; private set; } = string.Empty;
    public string StreamName { get; private set; } = string.Empty;
    public string TenantId { get; private set; } = string.Empty;
    public string ClientId { get; private set; } = string.Empty;
    public string SecretRef { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }

    private SentinelConnectorConfiguration() { }

    public static SentinelConnectorConfiguration Create(
        bool enabled,
        string dceEndpoint,
        string dcrImmutableId,
        string streamName,
        string tenantId,
        string clientId,
        string secretRef
    )
    {
        return new SentinelConnectorConfiguration
        {
            Id = Guid.NewGuid(),
            Enabled = enabled,
            DceEndpoint = dceEndpoint,
            DcrImmutableId = dcrImmutableId,
            StreamName = streamName,
            TenantId = tenantId,
            ClientId = clientId,
            SecretRef = secretRef,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(
        bool enabled,
        string dceEndpoint,
        string dcrImmutableId,
        string streamName,
        string tenantId,
        string clientId,
        string secretRef
    )
    {
        Enabled = enabled;
        DceEndpoint = dceEndpoint;
        DcrImmutableId = dcrImmutableId;
        StreamName = streamName;
        TenantId = tenantId;
        ClientId = clientId;
        SecretRef = secretRef;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
