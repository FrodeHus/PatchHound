using System.Text;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Models.Decisions;
using PatchHound.Core.Common;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Services;

public class ThreatIntelGenerationService(
    PatchHoundDbContext dbContext,
    TenantAiTextGenerationService aiTextGenerationService,
    ITenantAiConfigurationResolver aiConfigurationResolver
)
{
    private const string SystemPrompt =
        "You are a security analyst. You will receive a list of CVE vulnerabilities affecting a software product. " +
        "Use web research to retrieve current threat intelligence data for each vulnerability. " +
        "Write a concise threat intelligence summary in markdown covering: active exploitation status, " +
        "attack vectors and techniques (referencing MITRE ATT&CK where applicable), " +
        "available proof-of-concept or exploit availability, affected versions, " +
        "relevant threat actor activity, and recommended detection or mitigation strategies. " +
        "Include direct links to relevant advisories, NVD entries, vendor bulletins, PoC repositories, " +
        "or threat reports you find. Use clear markdown headings and bullet lists. " +
        "Do not invent facts — only report what you can verify via web research.";

    public async Task<bool> CanGenerateAsync(Guid tenantId, CancellationToken ct)
    {
        var result = await aiConfigurationResolver.ResolveDefaultAsync(tenantId, ct);
        return result.IsSuccess;
    }

    public async Task<Result<ThreatIntelDto>> GenerateAsync(
        Guid tenantId,
        Guid caseId,
        CancellationToken ct)
    {
        var case_ = await dbContext.RemediationCases
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == caseId, ct);
        if (case_ is null)
            return Result<ThreatIntelDto>.Failure("Remediation case not found.");

        var softwareProductId = case_.SoftwareProductId;

        var softwareName = await dbContext.SoftwareProducts.AsNoTracking()
            .Where(p => p.Id == softwareProductId)
            .Select(p => $"{p.Vendor} {p.Name}".Trim())
            .FirstOrDefaultAsync(ct) ?? "Unknown software";

        var openVulnIds = dbContext.DeviceVulnerabilityExposures.AsNoTracking()
            .Where(e => e.TenantId == tenantId
                && e.SoftwareProductId == softwareProductId
                && e.Status == ExposureStatus.Open)
            .Select(e => e.VulnerabilityId)
            .Distinct();

        var topVulns = await dbContext.Vulnerabilities.AsNoTracking()
            .Where(v => openVulnIds.Contains(v.Id))
            .Select(v => new
            {
                v.ExternalId,
                v.Title,
                VendorSeverity = v.VendorSeverity,
                v.CvssScore,
                v.Description,
            })
            .OrderByDescending(v => v.VendorSeverity)
            .ThenByDescending(v => v.CvssScore)
            .Take(5)
            .ToListAsync(ct);

        if (topVulns.Count == 0)
            return Result<ThreatIntelDto>.Failure("No open vulnerabilities found for this remediation case.");

        var vulnList = new StringBuilder();
        foreach (var v in topVulns)
        {
            vulnList.AppendLine($"- {v.ExternalId} ({v.VendorSeverity}, CVSS {v.CvssScore?.ToString("F1") ?? "N/A"}): {v.Title}");
            if (!string.IsNullOrWhiteSpace(v.Description))
                vulnList.AppendLine($"  {v.Description.Trim()}");
        }

        var userPrompt = $"Software product: {softwareName}\n\nTop vulnerabilities (by severity):\n{vulnList}";

        var request = new AiTextGenerationRequest(
            SystemPrompt,
            userPrompt,
            ExternalContext: null,
            UseProviderNativeWebResearch: true,
            MaxResearchSources: 10,
            IncludeCitations: true
        );

        var generated = await aiTextGenerationService.GenerateAsync(tenantId, null, request, ct);
        if (!generated.IsSuccess)
            return Result<ThreatIntelDto>.Failure(generated.Error ?? "AI generation failed.");

        var result = generated.Value;
        case_.SetThreatIntel(result.Content, result.ProfileName);
        await dbContext.SaveChangesAsync(ct);

        return Result<ThreatIntelDto>.Success(new ThreatIntelDto(
            result.Content,
            case_.ThreatIntelGeneratedAt,
            result.ProfileName,
            CanGenerate: true,
            UnavailableMessage: null
        ));
    }
}
