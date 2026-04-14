namespace PatchHound.Api.Models.Vulnerabilities;

public record AiReportDto(
    Guid Id,
    Guid VulnerabilityId,
    string Content,
    string ProviderType,
    string ProfileName,
    string Model,
    DateTimeOffset GeneratedAt
);
