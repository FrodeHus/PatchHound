using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class ExecutiveDashboardBriefingService(
    PatchHoundDbContext dbContext,
    ITenantAiConfigurationResolver aiConfigurationResolver,
    TenantAiTextGenerationService textGenerationService,
    ILogger<ExecutiveDashboardBriefingService> logger
)
{
    private const int TopSoftwareLimit = 5;

    public async Task<ExecutiveDashboardBriefing> RefreshAsync(Guid tenantId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var windowStartedAt = now.AddHours(-24);
        var input = await BuildInputAsync(tenantId, windowStartedAt, now, ct);
        var fallback = BuildDeterministicBriefing(input);
        var generatedContent = await TryGenerateWithAiAsync(tenantId, input, ct);
        var content = string.IsNullOrWhiteSpace(generatedContent) ? fallback : generatedContent.Trim();
        var usedAi = !string.IsNullOrWhiteSpace(generatedContent);

        var existing = await dbContext.ExecutiveDashboardBriefings
            .FirstOrDefaultAsync(item => item.TenantId == tenantId, ct);

        if (existing is null)
        {
            existing = ExecutiveDashboardBriefing.Create(
                tenantId,
                content,
                now,
                windowStartedAt,
                now,
                input.HighCriticalAppearedCount,
                input.ResolvedCount,
                usedAi
            );
            await dbContext.ExecutiveDashboardBriefings.AddAsync(existing, ct);
        }
        else
        {
            existing.Update(
                content,
                now,
                windowStartedAt,
                now,
                input.HighCriticalAppearedCount,
                input.ResolvedCount,
                usedAi
            );
        }

        await dbContext.SaveChangesAsync(ct);
        return existing;
    }

    public async Task<string> BuildFallbackAsync(Guid tenantId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var input = await BuildInputAsync(tenantId, now.AddHours(-24), now, ct);
        return BuildDeterministicBriefing(input);
    }

    private async Task<ExecutiveBriefingInput> BuildInputAsync(
        Guid tenantId,
        DateTimeOffset windowStartedAt,
        DateTimeOffset windowEndedAt,
        CancellationToken ct
    )
    {
        var appearedCount = await dbContext.DeviceVulnerabilityExposures.AsNoTracking()
            .Where(item =>
                item.TenantId == tenantId
                && item.FirstObservedAt >= windowStartedAt
                && item.FirstObservedAt <= windowEndedAt
                && (
                    item.Vulnerability.VendorSeverity == Severity.Critical
                    || item.Vulnerability.VendorSeverity == Severity.High
                ))
            .Select(item => item.VulnerabilityId)
            .Distinct()
            .CountAsync(ct);

        var resolvedCount = await dbContext.ExposureEpisodes.AsNoTracking()
            .Where(item =>
                item.TenantId == tenantId
                && item.ClosedAt != null
                && item.ClosedAt >= windowStartedAt
                && item.ClosedAt <= windowEndedAt)
            .Select(item => item.Exposure.VulnerabilityId)
            .Distinct()
            .CountAsync(ct);

        var softwareRows = await dbContext.DeviceVulnerabilityExposures.AsNoTracking()
            .Where(item =>
                item.TenantId == tenantId
                && item.Status == ExposureStatus.Open
                && item.SoftwareProductId != null
                && (
                    item.Vulnerability.VendorSeverity == Severity.Critical
                    || item.Vulnerability.VendorSeverity == Severity.High
                ))
            .Select(item => new
            {
                SoftwareName = item.SoftwareProduct!.Name,
                item.DeviceId,
                item.VulnerabilityId,
                item.Vulnerability.ExternalId,
                item.Vulnerability.Title,
                item.Vulnerability.VendorSeverity,
            })
            .ToListAsync(ct);

        var topSoftware = softwareRows
            .GroupBy(item => item.SoftwareName)
            .Select(group =>
            {
                var vulnerabilities = group
                    .GroupBy(item => new { item.VulnerabilityId, item.ExternalId, item.Title, item.VendorSeverity })
                    .Select(vulnerability => new ExecutiveBriefingVulnerability(
                        vulnerability.Key.ExternalId,
                        vulnerability.Key.Title,
                        vulnerability.Key.VendorSeverity.ToString()
                    ))
                    .OrderBy(item => item.Severity == Severity.Critical.ToString() ? 0 : 1)
                    .ThenBy(item => item.ExternalId)
                    .ToList();

                return new ExecutiveBriefingSoftwareProduct(
                    group.Key,
                    vulnerabilities.Count(item => item.Severity == Severity.Critical.ToString()),
                    vulnerabilities.Count(item => item.Severity == Severity.High.ToString()),
                    group.Select(item => item.DeviceId).Distinct().Count(),
                    vulnerabilities.Take(3).ToList()
                );
            })
            .OrderByDescending(item => item.CriticalCount)
            .ThenByDescending(item => item.HighCount)
            .ThenByDescending(item => item.AffectedDeviceCount)
            .ThenBy(item => item.SoftwareName)
            .Take(TopSoftwareLimit)
            .ToList();

        return new ExecutiveBriefingInput(
            appearedCount,
            resolvedCount,
            topSoftware,
            windowStartedAt,
            windowEndedAt
        );
    }

    private async Task<string?> TryGenerateWithAiAsync(
        Guid tenantId,
        ExecutiveBriefingInput input,
        CancellationToken ct
    )
    {
        try
        {
            var profileResult = await aiConfigurationResolver.ResolveDefaultAsync(tenantId, ct);
            if (!profileResult.IsSuccess)
            {
                return null;
            }

            var profile = profileResult.Value.Profile;
            var request = new AiTextGenerationRequest(
                "You are writing the executive security briefing for PatchHound. Return concise plain text only. Start with one sentence covering high/critical issues that appeared in the last 24 hours and issues resolved. Then add a short 'Top software risks:' section with up to five bullets, one per software product. Each bullet must name the software product and give a one-line risk summary. Do not invent products or counts.",
                JsonSerializer.Serialize(input, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            );

            var result = await textGenerationService.GenerateAsync(tenantId, profile.Id, request, ct);
            return result.IsSuccess ? result.Value.Content : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "AI executive dashboard briefing generation failed for tenant {TenantId}", tenantId);
            return null;
        }
    }

    private static string BuildDeterministicBriefing(ExecutiveBriefingInput input)
    {
        var lines = new List<string>
        {
            $"In the last 24 hours, {input.HighCriticalAppearedCount} high or critical issues appeared and {input.ResolvedCount} issues were resolved.",
            string.Empty,
            "Top software risks:",
        };

        if (input.TopSoftwareProducts.Count == 0)
        {
            lines.Add("- No critical or high software-product exposure is currently active.");
            return string.Join(Environment.NewLine, lines);
        }

        foreach (var product in input.TopSoftwareProducts)
        {
            var severityText = product.CriticalCount > 0
                ? $"{product.CriticalCount} critical"
                : $"{product.HighCount} high";
            var extraHigh = product.CriticalCount > 0 && product.HighCount > 0
                ? $" and {product.HighCount} high"
                : string.Empty;
            var examples = product.ExampleVulnerabilities.Count == 0
                ? "active exposure"
                : string.Join(", ", product.ExampleVulnerabilities.Select(item => item.ExternalId));

            lines.Add(
                $"- {product.SoftwareName}: {severityText}{extraHigh} vulnerabilities across {product.AffectedDeviceCount} devices; key exposure includes {examples}."
            );
        }

        return string.Join(Environment.NewLine, lines);
    }

    private sealed record ExecutiveBriefingInput(
        int HighCriticalAppearedCount,
        int ResolvedCount,
        IReadOnlyList<ExecutiveBriefingSoftwareProduct> TopSoftwareProducts,
        DateTimeOffset WindowStartedAt,
        DateTimeOffset WindowEndedAt
    );

    private sealed record ExecutiveBriefingSoftwareProduct(
        string SoftwareName,
        int CriticalCount,
        int HighCount,
        int AffectedDeviceCount,
        IReadOnlyList<ExecutiveBriefingVulnerability> ExampleVulnerabilities
    );

    private sealed record ExecutiveBriefingVulnerability(
        string ExternalId,
        string Title,
        string Severity
    );
}
