namespace Vigil.Infrastructure.Options;

public class DefenderOptions
{
    public const string SectionName = "Defender";

    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = "https://api.securitycenter.microsoft.com";
}
