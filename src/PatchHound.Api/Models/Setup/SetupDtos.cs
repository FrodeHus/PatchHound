namespace PatchHound.Api.Models.Setup;

public record SetupStatusDto(bool IsInitialized);

public record SetupCompleteRequest(string TenantName);
