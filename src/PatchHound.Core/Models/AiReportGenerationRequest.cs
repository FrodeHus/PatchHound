using PatchHound.Core.Entities;

namespace PatchHound.Core.Models;

public record AiReportGenerationRequest(
    Vulnerability VulnerabilityDefinition,
    IReadOnlyList<Device> AffectedAssets
);
