using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.Infrastructure;

namespace PatchHound.Tests.Infrastructure.Services;

public class ThreatAssessmentServiceTests
{
    [Fact]
    public async Task Upserts_new_assessment_for_vulnerability()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var v = Vulnerability.Create("nvd", "CVE-2026-1000", "t", "d", Severity.High, 7.5m, "v", DateTimeOffset.UtcNow);
        db.Vulnerabilities.Add(v);
        await db.SaveChangesAsync();

        var svc = new ThreatAssessmentService(db);
        var a = await svc.UpsertAsync(
            v.Id,
            threatScore: 7.8m, technicalScore: 7.5m, exploitLikelihoodScore: 0.6m, threatActivityScore: 6.0m,
            epssScore: 0.2m, knownExploited: false, publicExploit: true, activeAlert: false,
            hasRansomwareAssociation: false, hasMalwareAssociation: false,
            factorsJson: "[]", calculationVersion: "v1",
            ct: CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(v.Id, a.VulnerabilityId);
        Assert.Single(await db.ThreatAssessments.ToListAsync());
    }

    [Fact]
    public async Task Updates_existing_assessment_in_place()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var v = Vulnerability.Create("nvd", "CVE-2026-2000", "t", "d", Severity.High, 7.5m, "v", DateTimeOffset.UtcNow);
        db.Vulnerabilities.Add(v);
        await db.SaveChangesAsync();

        var svc = new ThreatAssessmentService(db);
        await svc.UpsertAsync(v.Id, 1, 1, 0.1m, 1, null, false, false, false, false, false, "[]", "v1", CancellationToken.None);
        await db.SaveChangesAsync();
        await svc.UpsertAsync(v.Id, 9, 9, 0.9m, 9, 0.8m, true, true, true, true, true, "[\"kev\"]", "v2", CancellationToken.None);
        await db.SaveChangesAsync();

        var assessments = await db.ThreatAssessments.ToListAsync();
        Assert.Single(assessments);
        Assert.Equal(9, assessments[0].ThreatScore);
        Assert.Equal("v2", assessments[0].CalculationVersion);
    }
}
