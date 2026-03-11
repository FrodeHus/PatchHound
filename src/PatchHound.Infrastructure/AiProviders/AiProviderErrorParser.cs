using System.Text.Json;

namespace PatchHound.Infrastructure.AiProviders;

internal static class AiProviderErrorParser
{
    public static string FormatHttpError(string providerName, int statusCode, string? reasonPhrase, string body)
    {
        var detail = ExtractMessage(body);
        if (!string.IsNullOrWhiteSpace(detail))
        {
            return $"{providerName} returned {statusCode} {reasonPhrase}: {detail}";
        }

        return $"{providerName} returned {statusCode} {reasonPhrase}.";
    }

    private static string ExtractMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        var trimmed = body.Trim();

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("error", out var errorElement))
                {
                    if (errorElement.ValueKind == JsonValueKind.String)
                    {
                        return errorElement.GetString()?.Trim() ?? string.Empty;
                    }

                    if (
                        errorElement.ValueKind == JsonValueKind.Object
                        && errorElement.TryGetProperty("message", out var messageElement)
                        && messageElement.ValueKind == JsonValueKind.String
                    )
                    {
                        return messageElement.GetString()?.Trim() ?? string.Empty;
                    }
                }

                if (
                    root.TryGetProperty("message", out var rootMessage)
                    && rootMessage.ValueKind == JsonValueKind.String
                )
                {
                    return rootMessage.GetString()?.Trim() ?? string.Empty;
                }
            }
        }
        catch (JsonException)
        {
        }

        return trimmed;
    }
}
