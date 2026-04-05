namespace PatchHound.Core.Entities.AuthenticatedScans;

public class ScanningToolVersion
{
    public Guid Id { get; private set; }
    public Guid ScanningToolId { get; private set; }
    public int VersionNumber { get; private set; }
    public string ScriptContent { get; private set; } = string.Empty;
    public Guid EditedByUserId { get; private set; }
    public DateTimeOffset EditedAt { get; private set; }

    private ScanningToolVersion() { }

    public static ScanningToolVersion Create(
        Guid scanningToolId,
        int versionNumber,
        string scriptContent,
        Guid editedByUserId)
    {
        return new ScanningToolVersion
        {
            Id = Guid.NewGuid(),
            ScanningToolId = scanningToolId,
            VersionNumber = versionNumber,
            ScriptContent = scriptContent,
            EditedByUserId = editedByUserId,
            EditedAt = DateTimeOffset.UtcNow,
        };
    }
}
