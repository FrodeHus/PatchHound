namespace PatchHound.Infrastructure.Options;

public class SmtpOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "noreply@patchhound.local";
    public bool EnableSsl { get; set; } = true;
}
