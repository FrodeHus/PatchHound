namespace PatchHound.Core.Entities.Ingestion;

public class IngestionRunDelta
{
    public Guid Id { get; private set; }
    public Guid RunId { get; private set; }
    public Guid TenantId { get; private set; }
    public string Kind { get; private set; } = string.Empty;
    public Guid EntityId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private IngestionRunDelta() { }

    public static IngestionRunDelta Create(
        Guid runId,
        Guid tenantId,
        string kind,
        Guid entityId,
        DateTimeOffset createdAt)
    {
        IngestionEntityValidation.RequireGuid(runId, nameof(runId));
        IngestionEntityValidation.RequireGuid(tenantId, nameof(tenantId));
        IngestionEntityValidation.RequireGuid(entityId, nameof(entityId));
        if (createdAt == default)
        {
            throw new ArgumentException("CreatedAt is required.", nameof(createdAt));
        }

        return new IngestionRunDelta
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            TenantId = tenantId,
            Kind = IngestionEntityValidation.RequireString(kind, nameof(kind), 64),
            EntityId = entityId,
            CreatedAt = createdAt,
        };
    }
}
