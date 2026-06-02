using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models.OperationalContext;
using PatchHound.Core.Services.OperationalContext;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services.OperationalContext;

public sealed class AiOperationalContextService : IAiOperationalContextService
{
    private readonly PatchHoundDbContext db;
    private readonly OperationalContextRedactor redactor;
    private readonly OperationalContextTruncator truncator;

    public AiOperationalContextService(
        PatchHoundDbContext db,
        IPromptTokenEstimator estimator,
        OperationalContextRedactor redactor,
        OperationalContextTruncator truncator)
    {
        // estimator is accepted for DI/caller parity; token estimation is performed by the
        // truncator (which holds its own estimator). Kept on the ctor so the service binding
        // stays stable when a provider-specific estimator replaces the heuristic default.
        _ = estimator;
        this.db = db;
        this.redactor = redactor;
        this.truncator = truncator;
    }

    public async Task<AiOperationalContextResult> BuildForRemediationCaseAsync(
        Guid tenantId, Guid remediationCaseId, AiOperationalContextOptions options, CancellationToken ct)
    {
        // IgnoreQueryFilters + explicit tenant predicate: correct under both API (user) and
        // worker (system) contexts. The explicit TenantId filter is the security boundary.
        var rc = await db.RemediationCases.IgnoreQueryFilters()
            .Where(c => c.Id == remediationCaseId && c.TenantId == tenantId)
            .Select(c => new
            {
                c.Id,
                c.SoftwareProductId,
                c.Status,
                Product = db.SoftwareProducts.Where(p => p.Id == c.SoftwareProductId)
                    .Select(p => new { p.Name, p.Vendor }).FirstOrDefault(),
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException(
                $"Remediation case {remediationCaseId} not found for tenant {tenantId}.");

        var exposures = db.DeviceVulnerabilityExposures.IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId
                && e.SoftwareProductId == rc.SoftwareProductId
                && e.Status == ExposureStatus.Open
                && e.Device.ActiveInTenant
                && e.Device.HealthStatus == "Active");

        var openExposureCount = await exposures.CountAsync(ct);
        var deviceIds = await exposures.Select(e => e.DeviceId).Distinct().ToListAsync(ct);

        var criticality = await db.Devices.IgnoreQueryFilters()
            .Where(d => d.TenantId == tenantId && deviceIds.Contains(d.Id))
            .GroupBy(d => d.Criticality)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var softwareRisk = await db.SoftwareRiskScores.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && s.SoftwareProductId == rc.SoftwareProductId)
            .Select(s => (decimal?)s.OverallScore).FirstOrDefaultAsync(ct);

        var maxDeviceRisk = await db.DeviceRiskScores.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && deviceIds.Contains(s.DeviceId))
            .Select(s => (decimal?)s.OverallScore).OrderByDescending(s => s).FirstOrDefaultAsync(ct);

        var pack = new OperationalContextPack
        {
            ContextKind = "RemediationCase",
            Subject = new()
            {
                RemediationCaseId = rc.Id,
                SoftwareProduct = rc.Product is null ? null : new()
                {
                    Id = rc.SoftwareProductId,
                    Name = rc.Product.Name,
                    Vendor = rc.Product.Vendor,
                },
            },
            Scope = new()
            {
                OpenExposureCount = openExposureCount,
                AffectedDeviceCount = deviceIds.Count,
                CriticalityDistribution = criticality.ToDictionary(x => x.Key.ToString(), x => x.Count),
            },
            Risk = new() { SoftwareRiskScore = softwareRisk, MaxDeviceRiskScore = maxDeviceRisk },
            Workflow = new() { Status = rc.Status.ToString() },
            Limits = new() { TopDeviceLimit = options.TopDeviceLimit, ExposureLimit = options.ExposureLimit },
        };

        return Finalize(pack, options);
    }

    public async Task<AiOperationalContextResult> BuildForVulnerabilityAsync(
        Guid tenantId, Guid vulnerabilityId, AiOperationalContextOptions options, CancellationToken ct)
    {
        var vuln = await db.Vulnerabilities.IgnoreQueryFilters()
            .Where(v => v.Id == vulnerabilityId)
            .Select(v => new { v.Id, v.ExternalId, v.VendorSeverity })
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Vulnerability {vulnerabilityId} not found.");

        var exposures = db.DeviceVulnerabilityExposures.IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId
                && e.VulnerabilityId == vulnerabilityId
                && e.Status == ExposureStatus.Open
                && e.Device.ActiveInTenant
                && e.Device.HealthStatus == "Active");

        var openExposureCount = await exposures.CountAsync(ct);
        var deviceIds = await exposures.Select(e => e.DeviceId).Distinct().ToListAsync(ct);

        var criticality = await db.Devices.IgnoreQueryFilters()
            .Where(d => d.TenantId == tenantId && deviceIds.Contains(d.Id))
            .GroupBy(d => d.Criticality)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // Top-N devices by risk score, for citations.
        var topDevices = await db.DeviceRiskScores.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && deviceIds.Contains(s.DeviceId))
            .OrderByDescending(s => s.OverallScore)
            .Take(options.TopDeviceLimit)
            .Join(db.Devices.IgnoreQueryFilters(), s => s.DeviceId, d => d.Id,
                (s, d) => new { d.Id, d.Name, d.Criticality, s.OverallScore })
            .ToListAsync(ct);

        var citations = topDevices.Select((d, i) => new OperationalContextCitation
        {
            Key = $"device-risk-top-{i + 1}",
            EntityType = "Device",
            EntityId = d.Id,
            Label = d.Name,
            Fact = $"Device {d.Name} risk score {d.OverallScore:0}, {d.Criticality} asset",
            RiskWeight = (double)d.OverallScore,
        }).ToList();

        var pack = new OperationalContextPack
        {
            ContextKind = "Vulnerability",
            Subject = new() { VulnerabilityId = vuln.Id, VulnerabilityExternalId = vuln.ExternalId },
            Scope = new()
            {
                OpenExposureCount = openExposureCount,
                AffectedDeviceCount = deviceIds.Count,
                CriticalityDistribution = criticality.ToDictionary(x => x.Key.ToString(), x => x.Count),
            },
            Risk = new() { HighestVendorSeverity = vuln.VendorSeverity.ToString() },
            Citations = citations,
            Limits = new() { TopDeviceLimit = options.TopDeviceLimit, ExposureLimit = options.ExposureLimit },
        };

        return Finalize(pack, options);
    }

    private AiOperationalContextResult Finalize(OperationalContextPack pack, AiOperationalContextOptions options)
    {
        pack.Citations = redactor.RedactCitations(pack.Citations, options).ToList();
        var (fitted, tokens) = truncator.Fit(pack, options.MaxTokens);
        var json = JsonSerializer.Serialize(fitted, OperationalContextPack.SerializerOptions);
        return new AiOperationalContextResult
        {
            Pack = fitted,
            PackJson = json,
            Citations = fitted.Citations,
            TokenEstimate = tokens,
            Truncated = fitted.Limits.Truncated,
        };
    }
}
