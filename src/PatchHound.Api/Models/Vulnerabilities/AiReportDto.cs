namespace PatchHound.Api.Models.Vulnerabilities;

public record AiReportDto(
    Guid Id,
    Guid TenantVulnerabilityId,
    string Content,
    string ProviderType,
    string ProfileName,
    string Model,
    DateTimeOffset GeneratedAt
);
