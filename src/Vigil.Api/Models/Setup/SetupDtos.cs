namespace Vigil.Api.Models.Setup;

public record SetupStatusDto(bool IsInitialized);

public record SetupCompleteRequest(
    string TenantName,
    string EntraTenantId,
    string TenantSettings,
    string AdminEmail,
    string AdminDisplayName,
    string AdminEntraObjectId
);
