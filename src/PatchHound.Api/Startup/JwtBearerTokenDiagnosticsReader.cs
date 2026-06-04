using System.Text;
using System.Text.Json;

namespace PatchHound.Api.Startup;

public static class JwtBearerTokenDiagnosticsReader
{
    public static TokenDiagnostics? TryRead(string authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader))
        {
            return null;
        }

        const string bearerPrefix = "Bearer ";
        if (!authorizationHeader.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = authorizationHeader[bearerPrefix.Length..].Trim();
        var segments = token.Split('.');
        if (segments.Length < 2)
        {
            return null;
        }

        try
        {
            var payload = segments[1].Replace('-', '+').Replace('_', '/');

            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            return new TokenDiagnostics(
                GetStringOrArray(root, "aud"),
                GetString(root, "azp"),
                GetString(root, "appid")
            );
        }
        catch
        {
            return null;
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static string? GetStringOrArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Array => string.Join(
                ", ",
                property.EnumerateArray().Select(item => item.ToString())
            ),
            _ => property.ToString(),
        };
    }
}

public sealed record TokenDiagnostics(string? Audience, string? AuthorizedParty, string? AppId);
