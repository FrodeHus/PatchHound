using PatchHound.Core.Interfaces;

namespace PatchHound.Infrastructure.Services;

public class ConfigurableEmailSender(
    NotificationEmailConfigurationResolver configurationResolver,
    SmtpEmailSender smtpEmailSender,
    MailgunEmailSender mailgunEmailSender
) : IEmailSender
{
    public async Task SendEmailAsync(
        string to,
        string subject,
        string htmlBody,
        CancellationToken ct = default
    )
    {
        var configuration = await configurationResolver.GetAsync(ct);
        if (
            string.Equals(configuration.ActiveProvider, "mailgun", StringComparison.OrdinalIgnoreCase)
            && configuration.Mailgun.Enabled
            && configuration.Mailgun.IsConfigured
        )
        {
            await mailgunEmailSender.SendEmailAsync(
                configuration.Mailgun,
                to,
                subject,
                htmlBody,
                ct
            );
            return;
        }

        await smtpEmailSender.SendEmailAsync(to, subject, htmlBody, ct);
    }
}
