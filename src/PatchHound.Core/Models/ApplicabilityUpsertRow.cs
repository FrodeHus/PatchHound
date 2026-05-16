namespace PatchHound.Core.Models;

public sealed record ApplicabilityUpsertRow(
    Guid VulnerabilityId,
    Guid? SoftwareProductId,
    string? CpeCriteria,
    string? VersionStartIncluding,
    string? VersionStartExcluding,
    string? VersionEndIncluding,
    string? VersionEndExcluding,
    bool Vulnerable);
