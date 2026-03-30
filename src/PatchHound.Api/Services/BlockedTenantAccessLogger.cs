using Microsoft.Extensions.Logging;
using PatchHound.Api.Auth;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Services;

public sealed class BlockedTenantAccessLogger
{
    private readonly AuditLogWriter _auditLogWriter;
    private readonly ILogger<BlockedTenantAccessLogger> _logger;

    public BlockedTenantAccessLogger(
        AuditLogWriter auditLogWriter,
        ILogger<BlockedTenantAccessLogger> logger
    )
    {
        _auditLogWriter = auditLogWriter;
        _logger = logger;
    }

    public async Task LogAsync(
        IReadOnlyCollection<BlockedTenantAccessAttempt> attempts,
        CancellationToken ct
    )
    {
        foreach (var attempt in attempts)
        {
            var tenantId = attempt.AttemptedTenantId ?? Guid.Empty;

            _logger.LogWarning(
                "Blocked cross-tenant access attempt. TenantId: {TenantId}. Path: {Path}. Method: {Method}. Reason: {Reason}",
                attempt.AttemptedTenantId,
                attempt.Path,
                attempt.Method,
                attempt.Reason
            );

            await _auditLogWriter.WriteAsync(
                tenantId,
                "TenantAccess",
                attempt.AttemptedTenantId ?? Guid.Empty,
                AuditAction.Denied,
                null,
                new
                {
                    attempt.Path,
                    attempt.Method,
                    attempt.Reason,
                    AttemptedTenantId = attempt.AttemptedTenantId,
                },
                ct
            );
        }
    }
}
