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

    /// <summary>
    /// True when user/team identity should be pseudonymized.
    /// Intentionally stricter than <see cref="RedactDeviceNames"/>: user names are redacted
    /// unless explicitly opted in (<see cref="IncludeUserNames"/> = true), regardless of whether
    /// the provider is external or local. Team names are preferred by default per the
    /// data-minimization principle in the spec, so opting out of redaction is an explicit choice.
    /// </summary>
    public bool RedactUserNames => !IncludeUserNames;
}
