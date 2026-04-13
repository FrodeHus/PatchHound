using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class SoftwareDescriptionGenerationService
{
    private sealed record AliasIdentity(
        string SourceSystem,
        string ExternalSoftwareId,
        string RawName,
        string? RawVendor,
        string? RawVersion,
        string AliasConfidence,
        string MatchReason
    );

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PatchHoundDbContext _dbContext;
    private readonly ITenantAiConfigurationResolver _configurationResolver;
    private readonly ITenantAiResearchService _researchService;
    private readonly TenantAiTextGenerationService _textGenerationService;

    public SoftwareDescriptionGenerationService(
        PatchHoundDbContext dbContext,
        ITenantAiConfigurationResolver configurationResolver,
        ITenantAiResearchService researchService,
        TenantAiTextGenerationService textGenerationService
    )
    {
        _dbContext = dbContext;
        _configurationResolver = configurationResolver;
        _researchService = researchService;
        _textGenerationService = textGenerationService;
    }

    public async Task<Result<SoftwareDescriptionGenerationResult>> GenerateAsync(
        Guid tenantId,
        Guid tenantSoftwareId,
        Guid? tenantAiProfileId,
        CancellationToken ct
    )
    {
        var software = await _dbContext
            .TenantSoftware
            .Where(item => item.Id == tenantSoftwareId && item.TenantId == tenantId)
            .Select(item => new
            {
                item.Id,
                item.TenantId,
                item.NormalizedSoftwareId,
                item.NormalizedSoftware.CanonicalName,
                item.NormalizedSoftware.CanonicalVendor,
                item.NormalizedSoftware.PrimaryCpe23Uri,
                item.NormalizedSoftware.Description,
            })
            .FirstOrDefaultAsync(ct);
        if (software is null)
        {
            return Result<SoftwareDescriptionGenerationResult>.Failure(
                "Tenant software was not found."
            );
        }

        var aliases = await _dbContext
            .NormalizedSoftwareAliases.AsNoTracking()
            .Where(item => item.NormalizedSoftwareId == software.NormalizedSoftwareId)
            .OrderBy(item => item.SourceSystem)
            .ThenBy(item => item.ExternalSoftwareId)
            .Select(item => new AliasIdentity(
                item.SourceSystem.ToString(),
                item.ExternalSoftwareId,
                item.RawName,
                item.RawVendor,
                item.RawVersion,
                item.AliasConfidence.ToString(),
                item.MatchReason
            ))
            .ToListAsync(ct);

        var identityCandidates = BuildIdentityCandidates(
            software.CanonicalVendor,
            software.CanonicalName,
            aliases
        );

        var observedVersions = await _dbContext
            .NormalizedSoftwareInstallations.AsNoTracking()
            .Where(item => item.TenantSoftwareId == tenantSoftwareId && item.IsActive)
            .Select(item => item.DetectedVersion)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct()
            .OrderBy(item => item)
            .Take(10)
            .Cast<string>()
            .ToListAsync(ct);

        // Phase-2 stub: NormalizedSoftwareVulnerabilityProjection deleted; restored in Phase 3.
        var activeVulnerabilityCount = 0;

        var resolvedProfileResult = tenantAiProfileId.HasValue
            ? await _configurationResolver.ResolveByIdAsync(tenantId, tenantAiProfileId.Value, ct)
            : await _configurationResolver.ResolveDefaultAsync(tenantId, ct);
        if (!resolvedProfileResult.IsSuccess)
        {
            return Result<SoftwareDescriptionGenerationResult>.Failure(
                resolvedProfileResult.Error ?? "Unable to resolve tenant AI configuration."
            );
        }

        var resolvedProfile = resolvedProfileResult.Value;
        var request = new AiTextGenerationRequest(
            "You are a PatchHound software analyst. Write a concise product-level markdown description for security and vulnerability analysts. Explain what the software product is, who publishes it, common enterprise use cases, and why it may appear in enterprise environments. Keep the description general to the product rather than version-specific. Use the strongest vendor/name evidence available from the provided identities and aliases. If the identity appears ambiguous or generic, say so briefly instead of guessing. Use at most two short paragraphs and up to three short bullet points.",
            BuildUserPrompt(
                software.CanonicalVendor,
                software.CanonicalName,
                software.PrimaryCpe23Uri,
                identityCandidates,
                observedVersions,
                activeVulnerabilityCount,
                new
                {
                    canonicalName = software.CanonicalName,
                    canonicalVendor = software.CanonicalVendor,
                    primaryCpe23Uri = software.PrimaryCpe23Uri,
                    identityCandidates,
                    observedVersions,
                    activeVulnerabilityCount,
                    aliases,
                }
            )
        );

        var profile = resolvedProfile.Profile;
        if (profile.AllowExternalResearch)
        {
            if (
                profile.WebResearchMode == TenantAiWebResearchMode.ProviderNative
                && profile.ProviderType == TenantAiProviderType.OpenAi
            )
            {
                request = request with
                {
                    UseProviderNativeWebResearch = true,
                    AllowedDomains = ParseAllowedDomains(profile.AllowedDomains),
                    MaxResearchSources = profile.MaxResearchSources,
                    IncludeCitations = profile.IncludeCitations,
                };
            }
            else if (profile.WebResearchMode == TenantAiWebResearchMode.PatchHoundManaged)
            {
                var researchResult = await _researchService.ResearchAsync(
                    resolvedProfile,
                    new AiWebResearchRequest(
                        BuildResearchQuery(identityCandidates),
                        ParseAllowedDomains(profile.AllowedDomains),
                        profile.MaxResearchSources,
                        profile.IncludeCitations
                    ),
                    ct
                );

                if (
                    researchResult.IsSuccess
                    && !string.IsNullOrWhiteSpace(researchResult.Value.Context)
                )
                {
                    request = request with { ExternalContext = researchResult.Value.Context };
                }
            }
        }

        var generationResult = await _textGenerationService.GenerateResolvedAsync(
            resolvedProfile,
            request,
            ct
        );
        if (!generationResult.IsSuccess)
        {
            return Result<SoftwareDescriptionGenerationResult>.Failure(
                generationResult.Error ?? "Software description generation failed."
            );
        }

        var normalizedSoftware = await _dbContext
            .NormalizedSoftware
            .FirstAsync(item => item.Id == software.NormalizedSoftwareId, ct);
        normalizedSoftware.UpdateDescription(
            generationResult.Value.Content,
            generationResult.Value.GeneratedAt,
            generationResult.Value.ProviderType,
            generationResult.Value.ProfileName,
            generationResult.Value.Model
        );
        await _dbContext.SaveChangesAsync(ct);

        return Result<SoftwareDescriptionGenerationResult>.Success(
            new SoftwareDescriptionGenerationResult(
                tenantSoftwareId,
                software.NormalizedSoftwareId,
                generationResult.Value.Content,
                generationResult.Value.ProviderType,
                generationResult.Value.ProfileName,
                generationResult.Value.Model,
                generationResult.Value.GeneratedAt
            )
        );
    }

    private static string BuildResearchQuery(IReadOnlyList<string> identityCandidates)
    {
        var strongest = identityCandidates.FirstOrDefault() ?? "enterprise software product";
        var alternates = identityCandidates.Skip(1).Take(2).ToList();
        return alternates.Count == 0
            ? $"What is the enterprise software product {strongest}?"
            : $"What is the enterprise software product {strongest}? Alternate names: {string.Join(", ", alternates)}";
    }

    private static IReadOnlyList<string> ParseAllowedDomains(string? allowedDomains)
    {
        return (allowedDomains ?? string.Empty)
            .Split([',', '\n', '\r', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> BuildIdentityCandidates(
        string? canonicalVendor,
        string canonicalName,
        IReadOnlyList<AliasIdentity> aliases
    )
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(canonicalName))
        {
            candidates.Add(
                string.IsNullOrWhiteSpace(canonicalVendor)
                    ? canonicalName.Trim()
                    : $"{canonicalVendor.Trim()} {canonicalName.Trim()}"
            );
        }

        foreach (var alias in aliases)
        {
            var rawName = alias.RawName;
            var rawVendor = alias.RawVendor;
            if (string.IsNullOrWhiteSpace(rawName))
            {
                continue;
            }

            var candidate = string.IsNullOrWhiteSpace(rawVendor)
                ? rawName.Trim()
                : $"{rawVendor.Trim()} {rawName.Trim()}";
            candidates.Add(candidate);
        }

        return candidates
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
    }

    private static string BuildUserPrompt(
        string? canonicalVendor,
        string canonicalName,
        string? primaryCpe23Uri,
        IReadOnlyList<string> identityCandidates,
        IReadOnlyList<string> observedVersions,
        int activeVulnerabilityCount,
        object payload
    )
    {
        return
            $"Primary software identity: {identityCandidates.FirstOrDefault() ?? canonicalName}\n"
            + $"Canonical name: {canonicalName}\n"
            + $"Canonical vendor: {(canonicalVendor ?? "Unknown")}\n"
            + $"Primary CPE: {(primaryCpe23Uri ?? "None")}\n"
            + $"Observed versions: {(observedVersions.Count == 0 ? "None" : string.Join(", ", observedVersions))}\n"
            + $"Open vulnerability count in tenant: {activeVulnerabilityCount}\n"
            + $"Known identity variants: {(identityCandidates.Count == 0 ? "None" : string.Join(" | ", identityCandidates))}\n\n"
            + "Use the structured payload below as source evidence for aliases and observed software identity:\n"
            + JsonSerializer.Serialize(payload, JsonOptions);
    }
}
