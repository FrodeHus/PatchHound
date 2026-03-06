namespace Vigil.Core.Models;

public record SetupRequest(
    string TenantName,
    string EntraTenantId,
    string TenantSettings,
    string AdminEmail,
    string AdminDisplayName,
    string AdminEntraObjectId
);
