using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class ThreatAssessmentService
{
    private readonly PatchHoundDbContext _db;

    public ThreatAssessmentService(PatchHoundDbContext db)
    {
        _db = db;
    }

    public async Task<ThreatAssessment> UpsertAsync(
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
        string calculationVersion,
        CancellationToken ct)
    {
        var existing = await _db.ThreatAssessments
            .FirstOrDefaultAsync(a => a.VulnerabilityId == vulnerabilityId, ct);

        if (existing is null)
        {
            var a = ThreatAssessment.Create(
                vulnerabilityId, threatScore, technicalScore, exploitLikelihoodScore, threatActivityScore,
                epssScore, knownExploited, publicExploit, activeAlert,
                hasRansomwareAssociation, hasMalwareAssociation, factorsJson, calculationVersion);
            _db.ThreatAssessments.Add(a);
            return a;
        }

        existing.Update(
            threatScore, technicalScore, exploitLikelihoodScore, threatActivityScore,
            epssScore, knownExploited, publicExploit, activeAlert,
            hasRansomwareAssociation, hasMalwareAssociation, factorsJson, calculationVersion);
        return existing;
    }
}
