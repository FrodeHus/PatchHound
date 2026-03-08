namespace PatchHound.Api.Models.Setup;

public record SetupStatusDto(bool IsInitialized, bool RequiresSetup);

public record SetupCompleteRequest(string TenantName);
