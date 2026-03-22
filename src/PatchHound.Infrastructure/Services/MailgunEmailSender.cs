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

        using var request = BuildRequest(
            configuration,
            $"/v3/{configuration.Domain}/messages",
            new Dictionary<string, string>
            {
                ["from"] = from,
                ["to"] = to,
                ["subject"] = subject,
                ["html"] = htmlBody,
                ["text"] = StripHtml(htmlBody),
            }
        );

        if (!string.IsNullOrWhiteSpace(configuration.ReplyToAddress))
        {
            request.Content = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["from"] = from,
                    ["to"] = to,
                    ["subject"] = subject,
                    ["html"] = htmlBody,
                    ["text"] = StripHtml(htmlBody),
                    ["h:Reply-To"] = configuration.ReplyToAddress!,
                }
            );
        }

        using var response = await httpClient.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task<MailgunValidationResult> ValidateAsync(
        MailgunNotificationConfiguration configuration,
        CancellationToken ct = default
    )
    {
        using var request = BuildRequest(
            configuration,
            $"/v4/domains/{configuration.Domain}",
            content: null
        );

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadErrorAsync(response, ct);
            return new MailgunValidationResult(false, error, null);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var payload = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var state = payload.RootElement.TryGetProperty("domain", out var domainElement)
            && domainElement.TryGetProperty("state", out var stateElement)
            ? stateElement.GetString()
            : null;
        var message = string.IsNullOrWhiteSpace(state)
            ? "Mailgun domain lookup succeeded."
            : $"Mailgun domain lookup succeeded. Domain state: {state}.";
        return new MailgunValidationResult(true, message, state);
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
            request.Content = new FormUrlEncodedContent(content);
        }

        return request;
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
