using System.Net;

namespace PatchHound.Infrastructure.Services;

public static class IngestionFailurePolicy
{
    public static bool IsTerminal(Exception ex)
    {
        return ex switch
        {
            IngestionAbortedException => true,
            IngestionTerminalException => true,
            ArgumentException => true,
            HttpRequestException httpEx when httpEx.StatusCode is HttpStatusCode.BadRequest
                or HttpStatusCode.Unauthorized
                or HttpStatusCode.Forbidden => true,
            _ => false,
        };
    }

    public static string Describe(Exception ex)
    {
        return ex switch
        {
            IngestionAbortedException
                => "Ingestion failed: the run was aborted by an operator.",
            HttpRequestException httpEx when httpEx.StatusCode == HttpStatusCode.TooManyRequests
                => "Ingestion failed: external API throttled (429 Too Many Requests) after the configured retry limit was exhausted.",
            HttpRequestException httpEx when httpEx.StatusCode == HttpStatusCode.BadRequest
                => "Ingestion failed: external API rejected the request (400 Bad Request).",
            HttpRequestException httpEx when httpEx.StatusCode == HttpStatusCode.Unauthorized
                => "Ingestion failed: external API authentication failed (401 Unauthorized).",
            HttpRequestException httpEx when httpEx.StatusCode == HttpStatusCode.Forbidden
                => "Ingestion failed: external API access was forbidden (403 Forbidden).",
            TimeoutException
                => "Ingestion failed: an external API request timed out and the run stopped so it can resume from the last committed checkpoint.",
            TaskCanceledException
                => "Ingestion failed: an external API request timed out and the run stopped so it can resume from the last committed checkpoint.",
            IngestionTerminalException terminal
                => $"Ingestion failed: {DescribeTerminalConfigurationFailure(terminal)}",
            _ => "Ingestion failed: an unexpected error stopped the run. Review worker logs for details.",
        };
    }

    private static string DescribeTerminalConfigurationFailure(IngestionTerminalException ex)
    {
        var message = ex.Message.Trim();

        if (
            message.Contains("credentials are incomplete", StringComparison.OrdinalIgnoreCase)
            || message.Contains("could not be resolved", StringComparison.OrdinalIgnoreCase)
        )
        {
            return "source configuration is incomplete or invalid. Review source credentials and settings before retrying.";
        }

        return "source configuration is invalid and requires operator action before the run can continue.";
    }
}
