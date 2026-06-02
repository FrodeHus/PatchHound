namespace PatchHound.Core.Models.OperationalContext;

public sealed class AiOperationalContextOptions
{
    public int MaxTokens { get; init; } = 3000;
    public bool ProviderIsExternal { get; init; }
    public bool IncludeDeviceNames { get; init; } = true;
    public bool IncludeUserNames { get; init; }
    public int TopDeviceLimit { get; init; } = 10;
    public int ExposureLimit { get; init; } = 100;
    public int TopLabelLimit { get; init; } = 5;
    public int TopTeamLimit { get; init; } = 5;
    public int TopSecurityProfileLimit { get; init; } = 5;

    /// <summary>True when names should be pseudonymized (external provider unless opted in).</summary>
    public bool RedactDeviceNames => ProviderIsExternal && !IncludeDeviceNames;
    public bool RedactUserNames => !IncludeUserNames;
}
