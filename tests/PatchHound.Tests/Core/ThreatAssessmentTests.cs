using PatchHound.Core.Entities;

namespace PatchHound.Tests.Core;

public class ThreatAssessmentTests
{
    [Fact]
    public void Create_sets_all_factors_and_timestamps()
    {
        var vulnId = Guid.NewGuid();
        var a = ThreatAssessment.Create(
            vulnerabilityId: vulnId,
            threatScore: 8.1m,
            technicalScore: 7.5m,
            exploitLikelihoodScore: 0.7m,
            threatActivityScore: 6.2m,
            epssScore: 0.33m,
            knownExploited: true,
            publicExploit: true,
            activeAlert: false,
            hasRansomwareAssociation: true,
            hasMalwareAssociation: false,
            factorsJson: "[]",
            calculationVersion: "v2");

        Assert.Equal(vulnId, a.VulnerabilityId);
        Assert.Equal(8.1m, a.ThreatScore);
        Assert.True(a.KnownExploited);
        Assert.False(a.ActiveAlert);
        Assert.Equal("v2", a.CalculationVersion);
        Assert.NotEqual(default, a.CalculatedAt);
    }

    [Fact]
    public void Update_refreshes_factors_and_CalculatedAt()
    {
        var a = ThreatAssessment.Create(Guid.NewGuid(), 1, 1, 0, 1, null, false, false, false, false, false, "[]", "v1");
        var original = a.CalculatedAt;
        Thread.Sleep(5);
        a.Update(9, 9, 1, 9, 0.99m, true, true, true, true, true, "[\"kev\"]", "v2");
        Assert.Equal(9, a.ThreatScore);
        Assert.True(a.CalculatedAt > original);
    }
}
