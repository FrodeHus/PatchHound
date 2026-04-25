namespace PatchHound.Infrastructure.Credentials;

public static class StoredCredentialTypes
{
    public const string EntraClientSecret = "entra-client-secret";
    public const string ApiKey = "api-key";

    public static string GetDisplayName(string type) =>
        string.Equals(type, EntraClientSecret, StringComparison.OrdinalIgnoreCase)
            ? "Entra ID identity"
            : string.Equals(type, ApiKey, StringComparison.OrdinalIgnoreCase)
                ? "API key"
            : type;
}
