namespace PatchHound.Core.Entities;

public class StagedCloudApplication
{
    public Guid Id { get; private set; }
    public Guid IngestionRunId { get; private set; }
    public Guid TenantId { get; private set; }
    public string SourceKey { get; private set; } = string.Empty;
    public string ExternalId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string PayloadJson { get; private set; } = string.Empty;
    public DateTimeOffset StagedAt { get; private set; }

    private StagedCloudApplication() { }

    public static StagedCloudApplication Create(
        Guid ingestionRunId,
        Guid tenantId,
        string sourceKey,
        string externalId,
        string name,
        string? description,
        string payloadJson,
        DateTimeOffset stagedAt
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            IngestionRunId = ingestionRunId,
            TenantId = tenantId,
            SourceKey = sourceKey,
            ExternalId = externalId,
            Name = name,
            Description = description,
            PayloadJson = payloadJson,
            StagedAt = stagedAt,
        };
}
