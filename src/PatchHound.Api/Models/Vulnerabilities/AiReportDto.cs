namespace PatchHound.Api.Models.Vulnerabilities;

public record AiReportDto(
    Guid Id,
    Guid TenantVulnerabilityId,
    string Content,
    string Provider,
    DateTimeOffset GeneratedAt
);
