namespace PatchHound.Core.Entities.AuthenticatedScans;

public class ScanJobValidationIssue
{
    public Guid Id { get; private set; }
    public Guid ScanJobId { get; private set; }
    public string FieldPath { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public int EntryIndex { get; private set; }

    private ScanJobValidationIssue() { }

    public static ScanJobValidationIssue Create(Guid scanJobId, string fieldPath, string message, int entryIndex) =>
        new()
        {
            Id = Guid.NewGuid(),
            ScanJobId = scanJobId,
            FieldPath = fieldPath,
            Message = message,
            EntryIndex = entryIndex,
        };
}
