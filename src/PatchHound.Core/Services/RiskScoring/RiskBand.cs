namespace PatchHound.Core.Services.RiskScoring;

public static class RiskBand
{
    public const decimal MediumThreshold = 500m;
    public const decimal HighThreshold = 700m;
    public const decimal CriticalThreshold = 850m;

    public static string FromScore(decimal score) => score switch
    {
        >= CriticalThreshold => "Critical",
        >= HighThreshold => "High",
        >= MediumThreshold => "Medium",
        > 0m => "Low",
        _ => "None",
    };
}
