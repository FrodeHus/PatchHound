namespace PatchHound.Api.Models.Software;

public record TenantSoftwareInstallationQuery(string? Version = null, bool ActiveOnly = true);
