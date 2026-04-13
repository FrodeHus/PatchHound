using PatchHound.Core.Entities;

namespace PatchHound.Core.Models;

// Phase-2: VulnerabilityDefinition replaced by canonical Vulnerability.
public record AiReportGenerationRequest(
    Vulnerability VulnerabilityDefinition,
    IReadOnlyList<Asset> AffectedAssets
);
