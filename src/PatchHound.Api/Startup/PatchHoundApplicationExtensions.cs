using Microsoft.EntityFrameworkCore;
using Npgsql;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Startup;

public static class PatchHoundApplicationExtensions
{
    public static async Task MigrateDatabaseOnStartupAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();

        Console.WriteLine("[startup] PatchHound.Api starting database migration check");
        var dbContext = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();
        var migrationLogger = scope
            .ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("StartupMigration");
        await MigrateWithRetryAsync(dbContext, migrationLogger, app.Lifetime.ApplicationStopping);
        Console.WriteLine("[startup] PatchHound.Api database migration check completed");
    }

    public static IApplicationBuilder UsePatchHoundSecurityHeaders(this IApplicationBuilder app)
    {
        return app.Use(
            async (context, next) =>
            {
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["X-Frame-Options"] = "DENY";
                context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                context.Response.Headers["Content-Security-Policy"] =
                    "default-src 'self'; connect-src 'self' wss:; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; object-src 'none'; frame-ancestors 'none';";
                await next();
            }
        );
    }

    private static async Task MigrateWithRetryAsync(
        PatchHoundDbContext dbContext,
        ILogger logger,
        CancellationToken ct)
    {
        const int maxAttempts = 12;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await dbContext.Database.MigrateAsync(ct);
                return;
            }
            catch (Exception ex)
                when (attempt < maxAttempts
                    && (
                        ex is NpgsqlException
                        || ex.InnerException is NpgsqlException
                        || ex is TimeoutException
                    )
                )
            {
                logger.LogWarning(
                    ex,
                    "Database migration startup attempt {Attempt}/{MaxAttempts} failed. Retrying in 5 seconds.",
                    attempt,
                    maxAttempts
                );
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }
}
