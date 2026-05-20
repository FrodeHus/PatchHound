namespace PatchHound.Core.Services.RiskScoring;

public static class TenantRiskBand
{
    public const decimal MediumThreshold = 200m;
    public const decimal HighThreshold = 400m;
    public const decimal CriticalThreshold = 600m;

    public static string FromScore(decimal score) => score switch
    {
        >= CriticalThreshold => "Critical",
        >= HighThreshold => "High",
        >= MediumThreshold => "Medium",
        > 0m => "Low",
        _ => "None",
    };
}
