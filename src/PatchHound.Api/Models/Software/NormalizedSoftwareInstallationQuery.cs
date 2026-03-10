namespace PatchHound.Api.Models.Software;

public record NormalizedSoftwareInstallationQuery(string? Version = null, bool ActiveOnly = true);
