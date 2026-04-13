namespace PatchHound.Core.Entities;

public class ThreatAssessment
{
    public Guid Id { get; private set; }
    public Guid VulnerabilityId { get; private set; }
    public decimal ThreatScore { get; private set; }
    public decimal TechnicalScore { get; private set; }
    public decimal ExploitLikelihoodScore { get; private set; }
    public decimal ThreatActivityScore { get; private set; }
    public decimal? EpssScore { get; private set; }
    public bool KnownExploited { get; private set; }
    public bool PublicExploit { get; private set; }
    public bool ActiveAlert { get; private set; }
    public bool HasRansomwareAssociation { get; private set; }
    public bool HasMalwareAssociation { get; private set; }
    public DateTimeOffset? DefenderLastRefreshedAt { get; private set; }
    public string FactorsJson { get; private set; } = "[]";
    public string CalculationVersion { get; private set; } = null!;
    public DateTimeOffset CalculatedAt { get; private set; }

    public Vulnerability Vulnerability { get; private set; } = null!;

    private ThreatAssessment() { }

    public static ThreatAssessment Create(
        Guid vulnerabilityId,
        decimal threatScore,
        decimal technicalScore,
        decimal exploitLikelihoodScore,
        decimal threatActivityScore,
        decimal? epssScore,
        bool knownExploited,
        bool publicExploit,
        bool activeAlert,
        bool hasRansomwareAssociation,
        bool hasMalwareAssociation,
        string factorsJson,
        string calculationVersion)
    {
        return new ThreatAssessment
        {
            Id = Guid.NewGuid(),
            VulnerabilityId = vulnerabilityId,
            ThreatScore = threatScore,
            TechnicalScore = technicalScore,
            ExploitLikelihoodScore = exploitLikelihoodScore,
            ThreatActivityScore = threatActivityScore,
            EpssScore = epssScore,
            KnownExploited = knownExploited,
            PublicExploit = publicExploit,
            ActiveAlert = activeAlert,
            HasRansomwareAssociation = hasRansomwareAssociation,
            HasMalwareAssociation = hasMalwareAssociation,
            FactorsJson = factorsJson,
            CalculationVersion = calculationVersion,
            CalculatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(
        decimal threatScore,
        decimal technicalScore,
        decimal exploitLikelihoodScore,
        decimal threatActivityScore,
        decimal? epssScore,
        bool knownExploited,
        bool publicExploit,
        bool activeAlert,
        bool hasRansomwareAssociation,
        bool hasMalwareAssociation,
        string factorsJson,
        string calculationVersion)
    {
        ThreatScore = threatScore;
        TechnicalScore = technicalScore;
        ExploitLikelihoodScore = exploitLikelihoodScore;
        ThreatActivityScore = threatActivityScore;
        EpssScore = epssScore;
        KnownExploited = knownExploited;
        PublicExploit = publicExploit;
        ActiveAlert = activeAlert;
        HasRansomwareAssociation = hasRansomwareAssociation;
        HasMalwareAssociation = hasMalwareAssociation;
        FactorsJson = factorsJson;
        CalculationVersion = calculationVersion;
        CalculatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkDefenderRefreshed(DateTimeOffset refreshedAt) =>
        DefenderLastRefreshedAt = refreshedAt;
}
