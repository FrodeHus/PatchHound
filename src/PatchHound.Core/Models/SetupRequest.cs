namespace PatchHound.Core.Models;

public record SetupRequest(
    string TenantName,
    string EntraTenantId,
    string AdminEmail,
    string AdminDisplayName,
    string AdminEntraObjectId
);
