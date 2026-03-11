namespace PatchHound.Api.Models.Software;

public record TenantSoftwareAiReportDto(
    Guid TenantSoftwareId,
    string Content,
    string ProviderType,
    string ProfileName,
    string Model,
    DateTimeOffset GeneratedAt
);
