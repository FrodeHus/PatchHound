namespace PatchHound.Core.Entities.AuthenticatedScans;

public class ScanProfileTool
{
    public Guid ScanProfileId { get; private set; }
    public Guid ScanningToolId { get; private set; }
    public int ExecutionOrder { get; private set; }

    private ScanProfileTool() { }

    public static ScanProfileTool Create(Guid scanProfileId, Guid scanningToolId, int executionOrder) =>
        new() { ScanProfileId = scanProfileId, ScanningToolId = scanningToolId, ExecutionOrder = executionOrder };
}
