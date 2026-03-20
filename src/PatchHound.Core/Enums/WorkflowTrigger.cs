namespace PatchHound.Core.Enums;

public enum WorkflowTrigger
{
    VulnerabilityDetected,
    VulnerabilityReopened,
    AssetOnboarded,
    ScheduledIngestion,
    ManualRun,
}
