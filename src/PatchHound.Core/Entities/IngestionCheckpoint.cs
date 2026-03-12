namespace PatchHound.Core.Entities;

public class IngestionCheckpoint
{
    public Guid Id { get; private set; }
    public Guid IngestionRunId { get; private set; }
    public Guid TenantId { get; private set; }
    public string SourceKey { get; private set; } = string.Empty;
    public string Phase { get; private set; } = string.Empty;
    public int BatchNumber { get; private set; }
    public string CursorJson { get; private set; } = string.Empty;
    public int RecordsCommitted { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset LastCommittedAt { get; private set; }

    private IngestionCheckpoint() { }

    public static IngestionCheckpoint Start(
        Guid ingestionRunId,
        Guid tenantId,
        string sourceKey,
        string phase,
        DateTimeOffset startedAt
    )
    {
        return new IngestionCheckpoint
        {
            Id = Guid.NewGuid(),
            IngestionRunId = ingestionRunId,
            TenantId = tenantId,
            SourceKey = sourceKey,
            Phase = phase,
            BatchNumber = 0,
            CursorJson = string.Empty,
            RecordsCommitted = 0,
            Status = "Running",
            LastCommittedAt = startedAt,
        };
    }

    public void CommitBatch(
        int batchNumber,
        string? cursorJson,
        int recordsCommitted,
        string status,
        DateTimeOffset committedAt
    )
    {
        BatchNumber = batchNumber;
        CursorJson = cursorJson ?? string.Empty;
        RecordsCommitted = recordsCommitted;
        Status = status;
        LastCommittedAt = committedAt;
    }
}
