namespace PatchHound.Core.Entities;

public class SentinelConnectorConfiguration
{
    public Guid Id { get; private set; }
    public bool Enabled { get; private set; }
    public string DceEndpoint { get; private set; } = string.Empty;
    public string DcrImmutableId { get; private set; } = string.Empty;
    public string StreamName { get; private set; } = string.Empty;
    public Guid? StoredCredentialId { get; private set; }
    public StoredCredential? StoredCredential { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private SentinelConnectorConfiguration() { }

    public static SentinelConnectorConfiguration Create(
        bool enabled,
        string dceEndpoint,
        string dcrImmutableId,
        string streamName,
        Guid? storedCredentialId
    )
    {
        return new SentinelConnectorConfiguration
        {
            Id = Guid.NewGuid(),
            Enabled = enabled,
            DceEndpoint = dceEndpoint,
            DcrImmutableId = dcrImmutableId,
            StreamName = streamName,
            StoredCredentialId = storedCredentialId,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(
        bool enabled,
        string dceEndpoint,
        string dcrImmutableId,
        string streamName,
        Guid? storedCredentialId
    )
    {
        Enabled = enabled;
        DceEndpoint = dceEndpoint;
        DcrImmutableId = dcrImmutableId;
        StreamName = streamName;
        StoredCredentialId = storedCredentialId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
