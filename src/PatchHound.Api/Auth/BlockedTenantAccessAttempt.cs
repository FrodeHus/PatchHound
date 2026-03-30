namespace PatchHound.Api.Auth;

public sealed record BlockedTenantAccessAttempt(
    Guid? AttemptedTenantId,
    string Reason,
    string Path,
    string Method
);
