namespace PatchHound.Api.Models.System;

public record NotificationProviderSettingsDto(
    string ActiveProvider,
    SmtpNotificationProviderDto Smtp,
    MailgunNotificationProviderDto Mailgun
);

public record SmtpNotificationProviderDto(
    string Host,
    int Port,
    string? Username,
    string FromAddress,
    bool EnableSsl
);

public record MailgunNotificationProviderDto(
    bool Enabled,
    string Region,
    string Domain,
    string FromAddress,
    string? FromName,
    string? ReplyToAddress,
    bool HasApiKey
);

public record UpdateNotificationProviderSettingsRequest(
    string ActiveProvider,
    UpdateMailgunNotificationProviderRequest Mailgun
);

public record UpdateMailgunNotificationProviderRequest(
    bool Enabled,
    string Region,
    string Domain,
    string FromAddress,
    string? FromName,
    string? ReplyToAddress,
    string ApiKey
);

public record NotificationProviderValidationResponseDto(
    bool IsValid,
    string Message,
    string? DomainState
);
