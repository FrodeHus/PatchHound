namespace PatchHound.Api.Models.Setup;

public record SetupStatusDto(bool IsInitialized, bool RequiresSetup);

public record SetupCompleteRequest(string TenantName, DefenderSetupRequest Defender);

public record DefenderSetupRequest(bool Enabled, string ClientId, string ClientSecret);
