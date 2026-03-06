using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using Vigil.Core.Interfaces;
using Vigil.Infrastructure.Options;

namespace Vigil.Infrastructure.Services;

public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;

    public SmtpEmailSender(IOptions<SmtpOptions> options)
    {
        _options = options.Value;
    }

    public async Task SendEmailAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.EnableSsl,
            Credentials = string.IsNullOrEmpty(_options.Username)
                ? null
                : new NetworkCredential(_options.Username, _options.Password),
        };

        var message = new MailMessage(_options.FromAddress, to, subject, htmlBody)
        {
            IsBodyHtml = true,
        };

        await client.SendMailAsync(message, ct);
    }
}
