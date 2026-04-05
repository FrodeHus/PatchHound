namespace PatchHound.Core.Entities.AuthenticatedScans;

public class ScanJobResult
{
    public Guid Id { get; private set; }
    public Guid ScanJobId { get; private set; }
    public string RawStdout { get; private set; } = string.Empty;
    public string RawStderr { get; private set; } = string.Empty;
    public string ParsedJson { get; private set; } = string.Empty;
    public DateTimeOffset CapturedAt { get; private set; }

    private ScanJobResult() { }

    public static ScanJobResult Create(Guid scanJobId, string rawStdout, string rawStderr, string parsedJson) =>
        new()
        {
            Id = Guid.NewGuid(),
            ScanJobId = scanJobId,
            RawStdout = rawStdout,
            RawStderr = rawStderr,
            ParsedJson = parsedJson,
            CapturedAt = DateTimeOffset.UtcNow,
        };
}
