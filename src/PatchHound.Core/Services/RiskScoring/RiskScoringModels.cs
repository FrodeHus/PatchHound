using PatchHound.Core.Enums;

namespace PatchHound.Core.Services.RiskScoring;

public sealed record RiskExposureInput(
    Guid DeviceId,
    Guid VulnerabilityId,
    decimal EnvironmentalCvss,
    Severity VendorSeverity,
    Criticality AssetCriticality,
    decimal? ThreatScore,
    decimal? EpssScore,
    bool KnownExploited,
    bool PublicExploit,
    bool ActiveAlert,
    bool HasRansomwareAssociation,
    bool HasMalwareAssociation,
    bool IsEmergencyPatchRecommended
)
{
    public static RiskExposureInput Critical(
        Guid deviceId,
        Guid vulnerabilityId,
        decimal environmentalCvss,
        Criticality assetCriticality = Criticality.Medium,
        decimal? threatScore = null,
        decimal? epssScore = null,
        bool knownExploited = false,
        bool publicExploit = false,
        bool activeAlert = false,
        bool hasRansomwareAssociation = false,
        bool hasMalwareAssociation = false,
        bool isEmergency = false) =>
        Create(
            deviceId,
            vulnerabilityId,
            environmentalCvss,
            Severity.Critical,
            assetCriticality,
            threatScore,
            epssScore,
            knownExploited,
            publicExploit,
            activeAlert,
            hasRansomwareAssociation,
            hasMalwareAssociation,
            isEmergency);

    public static RiskExposureInput High(
        Guid deviceId,
        Guid vulnerabilityId,
        decimal environmentalCvss,
        Criticality assetCriticality = Criticality.Medium,
        decimal? threatScore = null,
        decimal? epssScore = null,
        bool knownExploited = false,
        bool publicExploit = false,
        bool activeAlert = false,
        bool hasRansomwareAssociation = false,
        bool hasMalwareAssociation = false,
        bool isEmergency = false) =>
        Create(
            deviceId,
            vulnerabilityId,
            environmentalCvss,
            Severity.High,
            assetCriticality,
            threatScore,
            epssScore,
            knownExploited,
            publicExploit,
            activeAlert,
            hasRansomwareAssociation,
            hasMalwareAssociation,
            isEmergency);

    public static RiskExposureInput Medium(
        Guid deviceId,
        Guid vulnerabilityId,
        decimal environmentalCvss,
        Criticality assetCriticality = Criticality.Medium,
        decimal? threatScore = null,
        decimal? epssScore = null,
        bool knownExploited = false,
        bool publicExploit = false,
        bool activeAlert = false,
        bool hasRansomwareAssociation = false,
        bool hasMalwareAssociation = false,
        bool isEmergency = false) =>
        Create(
            deviceId,
            vulnerabilityId,
            environmentalCvss,
            Severity.Medium,
            assetCriticality,
            threatScore,
            epssScore,
            knownExploited,
            publicExploit,
            activeAlert,
            hasRansomwareAssociation,
            hasMalwareAssociation,
            isEmergency);

    public static RiskExposureInput Low(
        Guid deviceId,
        Guid vulnerabilityId,
        decimal environmentalCvss,
        Criticality assetCriticality = Criticality.Medium,
        decimal? threatScore = null,
        decimal? epssScore = null,
        bool knownExploited = false,
        bool publicExploit = false,
        bool activeAlert = false,
        bool hasRansomwareAssociation = false,
        bool hasMalwareAssociation = false,
        bool isEmergency = false) =>
        Create(
            deviceId,
            vulnerabilityId,
            environmentalCvss,
            Severity.Low,
            assetCriticality,
            threatScore,
            epssScore,
            knownExploited,
            publicExploit,
            activeAlert,
            hasRansomwareAssociation,
            hasMalwareAssociation,
            isEmergency);

    private static RiskExposureInput Create(
        Guid deviceId,
        Guid vulnerabilityId,
        decimal environmentalCvss,
        Severity vendorSeverity,
        Criticality assetCriticality,
        decimal? threatScore,
        decimal? epssScore,
        bool knownExploited,
        bool publicExploit,
        bool activeAlert,
        bool hasRansomwareAssociation,
        bool hasMalwareAssociation,
        bool isEmergency) =>
        new(
            deviceId,
            vulnerabilityId,
            environmentalCvss,
            vendorSeverity,
            assetCriticality,
            threatScore,
            epssScore,
            knownExploited,
            publicExploit,
            activeAlert,
            hasRansomwareAssociation,
            hasMalwareAssociation,
            isEmergency);
}

public sealed record RiskScoreResult(
    decimal OverallScore,
    decimal MaxDetectionScore,
    int CriticalCount,
    int HighCount,
    int MediumCount,
    int LowCount,
    string RiskBand,
    string FactorsJson
);

public sealed record RiskContributionFactor(string Name, string Description, decimal Impact);
