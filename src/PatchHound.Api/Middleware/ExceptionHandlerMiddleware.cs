using System.Net;
using Microsoft.AspNetCore.Mvc;
using PatchHound.Infrastructure.Secrets;

namespace PatchHound.Api.Middleware;

public class ExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlerMiddleware> _logger;

    public ExceptionHandlerMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlerMiddleware> logger
    )
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            if (ex is SecretStoreUnavailableException secretStoreException)
            {
                _logger.LogError(
                    secretStoreException,
                    "Secret store unavailable for {Method} {Path}",
                    context.Request.Method,
                    context.Request.Path
                );

                context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                context.Response.ContentType = "application/problem+json";

                var serviceUnavailable = new ProblemDetails
                {
                    Status = (int)HttpStatusCode.ServiceUnavailable,
                    Title = "Secret store unavailable",
                    Detail = "The secret store is temporarily unavailable. Please try again later.",
                    Type = "https://tools.ietf.org/html/rfc9110#section-15.6.4",
                };

                await context.Response.WriteAsJsonAsync(serviceUnavailable);
                return;
            }

            _logger.LogError(
                ex,
                "Unhandled exception for {Method} {Path}",
                context.Request.Method,
                context.Request.Path
            );

            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/problem+json";

            var problemDetails = new ProblemDetails
            {
                Status = (int)HttpStatusCode.InternalServerError,
                Title = "An unexpected error occurred",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            };

            await context.Response.WriteAsJsonAsync(problemDetails);
        }
    }
}
