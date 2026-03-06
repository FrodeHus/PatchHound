namespace Vigil.Api.Models.Vulnerabilities;

public record AiReportDto(
    Guid Id,
    Guid VulnerabilityId,
    string Content,
    string Provider,
    DateTimeOffset GeneratedAt
);
