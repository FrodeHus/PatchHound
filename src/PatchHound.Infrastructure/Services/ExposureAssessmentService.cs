using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class ExposureAssessmentService(
    PatchHoundDbContext db,
    EnvironmentalSeverityCalculator calculator)
{
    public async Task AssessForTenantAsync(Guid tenantId, DateTimeOffset calculatedAt, CancellationToken ct)
    {
        var exposures = await db.DeviceVulnerabilityExposures
            .Where(e => e.TenantId == tenantId)
            .Include(e => e.Vulnerability)
            .Include(e => e.Device)
            .ToListAsync(ct);

        var profileIds = exposures.Where(e => e.Device.SecurityProfileId is not null)
            .Select(e => e.Device.SecurityProfileId!.Value)
            .Distinct()
            .ToList();

        var profiles = await db.SecurityProfiles
            .Where(p => profileIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var existing = await db.ExposureAssessments
            .Where(a => a.TenantId == tenantId)
            .ToDictionaryAsync(a => a.DeviceVulnerabilityExposureId, ct);

        foreach (var exposure in exposures)
        {
            SecurityProfile? profile = null;
            if (exposure.Device.SecurityProfileId is { } pid && profiles.TryGetValue(pid, out var found))
            {
                profile = found;
            }

            var result = calculator.Calculate(exposure.Vulnerability, exposure.Device, profile);
            var baseCvss = exposure.Vulnerability.CvssScore ?? 0m;
            var envCvss = result.EffectiveScore ?? baseCvss;

            if (existing.TryGetValue(exposure.Id, out var assessment))
            {
                assessment.Update(baseCvss, envCvss, result.ReasonSummary, calculatedAt);
            }
            else
            {
                db.ExposureAssessments.Add(Core.Entities.ExposureAssessment.Create(
                    tenantId,
                    exposure.Id,
                    profile?.Id,
                    baseCvss,
                    envCvss,
                    result.ReasonSummary,
                    calculatedAt));
            }
        }
    }
}
