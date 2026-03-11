using PatchHound.Core.Entities;

namespace PatchHound.Core.Models;

public record AiReportGenerationRequest(
    VulnerabilityDefinition VulnerabilityDefinition,
    IReadOnlyList<Asset> AffectedAssets
);
