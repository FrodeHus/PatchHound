namespace PatchHound.Api.Models.Admin;

public record TenantDto(Guid Id, string Name, string EntraTenantId, string Settings);

public record UpdateTenantSettingsRequest(string Settings);
