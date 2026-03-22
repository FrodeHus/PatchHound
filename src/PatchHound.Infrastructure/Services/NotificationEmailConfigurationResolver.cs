using Microsoft.Extensions.Options;
using PatchHound.Infrastructure.Options;
using PatchHound.Infrastructure.Secrets;

namespace PatchHound.Infrastructure.Services;

public class NotificationEmailConfigurationResolver(
    ISecretStore secretStore,
    IOptions<SmtpOptions> smtpOptions
)
{
    public const string MailgunSecretPath = "system/notification-services/mailgun";

    public async Task<NotificationEmailConfiguration> GetAsync(CancellationToken ct)
    {
        var activeProviderValue = await secretStore.GetSecretAsync(
            MailgunSecretPath,
            "activeProvider",
            ct
        );
        var enabledValue = await secretStore.GetSecretAsync(MailgunSecretPath, "enabled", ct);
        var regionValue = await secretStore.GetSecretAsync(MailgunSecretPath, "region", ct);
        var domain = await secretStore.GetSecretAsync(MailgunSecretPath, "domain", ct);
        var fromAddress = await secretStore.GetSecretAsync(MailgunSecretPath, "fromAddress", ct);
        var fromName = await secretStore.GetSecretAsync(MailgunSecretPath, "fromName", ct);
        var replyToAddress = await secretStore.GetSecretAsync(MailgunSecretPath, "replyToAddress", ct);
        var apiKey = await secretStore.GetSecretAsync(MailgunSecretPath, "apiKey", ct);

        var mailgun = new MailgunNotificationConfiguration(
            string.Equals(enabledValue, "true", StringComparison.OrdinalIgnoreCase),
            NormalizeRegion(regionValue),
            domain?.Trim() ?? string.Empty,
            fromAddress?.Trim() ?? string.Empty,
            string.IsNullOrWhiteSpace(fromName) ? null : fromName.Trim(),
            string.IsNullOrWhiteSpace(replyToAddress) ? null : replyToAddress.Trim(),
            apiKey?.Trim() ?? string.Empty
        );

        return new NotificationEmailConfiguration(
            NormalizeProvider(activeProviderValue),
            mailgun,
            new SmtpNotificationConfiguration(
                smtpOptions.Value.Host,
                smtpOptions.Value.Port,
                smtpOptions.Value.Username,
                smtpOptions.Value.FromAddress,
                smtpOptions.Value.EnableSsl
            )
        );
    }

    public static string NormalizeRegion(string? value)
    {
        return string.Equals(value, "eu", StringComparison.OrdinalIgnoreCase) ? "eu" : "us";
    }

    public static string NormalizeProvider(string? value)
    {
        return string.Equals(value, "mailgun", StringComparison.OrdinalIgnoreCase)
            ? "mailgun"
            : "smtp";
    }

    public static string GetMailgunBaseUrl(string region)
    {
        return string.Equals(region, "eu", StringComparison.OrdinalIgnoreCase)
            ? "https://api.eu.mailgun.net"
            : "https://api.mailgun.net";
    }
}

public record NotificationEmailConfiguration(
    string ActiveProvider,
    MailgunNotificationConfiguration Mailgun,
    SmtpNotificationConfiguration Smtp
);

public record MailgunNotificationConfiguration(
    bool Enabled,
    string Region,
    string Domain,
    string FromAddress,
    string? FromName,
    string? ReplyToAddress,
    string ApiKey
)
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Domain)
        && !string.IsNullOrWhiteSpace(FromAddress)
        && !string.IsNullOrWhiteSpace(ApiKey);
}

public record SmtpNotificationConfiguration(
    string Host,
    int Port,
    string? Username,
    string FromAddress,
    bool EnableSsl
);
