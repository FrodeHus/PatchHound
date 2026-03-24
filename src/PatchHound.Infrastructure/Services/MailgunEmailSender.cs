using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PatchHound.Core.Interfaces;

namespace PatchHound.Infrastructure.Services;

public partial class MailgunEmailSender(HttpClient httpClient) : IEmailSender
{
    public Task SendEmailAsync(
        string to,
        string subject,
        string htmlBody,
        CancellationToken ct = default
    )
    {
        throw new NotSupportedException(
            "MailgunEmailSender requires resolved Mailgun configuration."
        );
    }

    public async Task SendEmailAsync(
        MailgunNotificationConfiguration configuration,
        string to,
        string subject,
        string htmlBody,
        CancellationToken ct = default
    )
    {
        var from = string.IsNullOrWhiteSpace(configuration.FromName)
            ? configuration.FromAddress
            : $"{configuration.FromName} <{configuration.FromAddress}>";

        var content = new Dictionary<string, string>
        {
            ["from"] = from,
            ["to"] = to,
            ["subject"] = subject,
            ["html"] = htmlBody,
            ["text"] = StripHtml(htmlBody),
        };

        if (!string.IsNullOrWhiteSpace(configuration.ReplyToAddress))
        {
            content["h:Reply-To"] = configuration.ReplyToAddress!;
        }

        using var request = BuildRequest(
            configuration,
            $"/v3/{configuration.Domain}/messages",
            content
        );

        using var response = await httpClient.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task<MailgunValidationResult> ValidateAsync(
        MailgunNotificationConfiguration configuration,
        CancellationToken ct = default
    )
    {
        var from = string.IsNullOrWhiteSpace(configuration.FromName)
            ? configuration.FromAddress
            : $"{configuration.FromName} <{configuration.FromAddress}>";

        using var request = BuildRequest(
            configuration,
            $"/v3/{configuration.Domain}/messages",
            new Dictionary<string, string>
            {
                ["from"] = from,
                ["to"] = configuration.FromAddress,
                ["subject"] = "PatchHound Mailgun validation",
                ["text"] = "PatchHound Mailgun validation request.",
                ["o:testmode"] = "yes",
            }
        );

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadErrorAsync(response, ct);
            return new MailgunValidationResult(false, error, null);
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        var message = "Mailgun accepted a test-mode message request.";
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var payload = JsonDocument.Parse(body);
                if (payload.RootElement.TryGetProperty("message", out var messageElement)
                    && !string.IsNullOrWhiteSpace(messageElement.GetString()))
                {
                    message = messageElement.GetString()!;
                }
            }
            catch (JsonException)
            {
                // Keep the generic success message when the response body is not JSON.
            }
        }

        return new MailgunValidationResult(true, message, "accepted");
    }

    private static HttpRequestMessage BuildRequest(
        MailgunNotificationConfiguration configuration,
        string path,
        IReadOnlyDictionary<string, string>? content
    )
    {
        var request = new HttpRequestMessage(
            content is null ? HttpMethod.Get : HttpMethod.Post,
            $"{NotificationEmailConfigurationResolver.GetMailgunBaseUrl(configuration.Region)}{path}"
        );
        var basicToken = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"api:{configuration.ApiKey}")
        );
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicToken);
        if (content is not null)
        {
            request.Content = BuildMultipartContent(content);
        }

        return request;
    }

    private static MultipartFormDataContent BuildMultipartContent(
        IReadOnlyDictionary<string, string> content
    )
    {
        var multipart = new MultipartFormDataContent();
        foreach (var (key, value) in content)
        {
            multipart.Add(new StringContent(value), key);
        }

        return multipart;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new InvalidOperationException(await ReadErrorAsync(response, ct));
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(body))
        {
            return $"Mailgun request failed with status {(int)response.StatusCode}.";
        }

        return $"Mailgun request failed with status {(int)response.StatusCode}: {body}";
    }

    private static string StripHtml(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var withoutTags = HtmlRegex().Replace(value, " ");
        return WhitespaceRegex().Replace(withoutTags, " ").Trim();
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}

public record MailgunValidationResult(bool IsValid, string Message, string? DomainState);
