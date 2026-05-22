using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Options;
using PatchHound.Infrastructure.Secrets;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Infrastructure.Services;

public sealed class JinaReaderAiResearchProvider(
    HttpClient httpClient,
    PatchHoundDbContext dbContext,
    ISecretStore secretStore,
    IOptions<AiResearchOptions>? options = null
) : IAiResearchSourceProvider
{
    private const string ExternalResearchUnavailableNote =
        "External research was not available during the time of assessment";

    public string SourceKey => EnrichmentSourceCatalog.JinaReaderSourceKey;

    private readonly AiResearchOptions _options = options?.Value ?? new AiResearchOptions();

    public async Task<Result<AiWebResearchBundle>> ResearchAsync(
        EnrichmentSourceConfiguration source,
        AiWebResearchRequest request,
        CancellationToken ct
    )
    {
        try
        {
            var apiKey = await ResolveApiKeyAsync(source, ct);
            var jinaOptions = JinaReaderOptions.FromJson(source.OptionsJson).Normalize();
            var searchUrl = ExternalWebSearchResearchProvider.BuildSearchUrl(
                BuildQuery(request),
                _options.JinaSearchProvider
            );

            var searchBody = await SendReaderRequestAsync(searchUrl, apiKey, jinaOptions, ct);
            if (string.IsNullOrWhiteSpace(searchBody))
            {
                return UnavailableResult();
            }

            var sources = ExternalWebSearchResearchProvider.ExtractSources(searchBody, request.MaxSources);
            var sourceContexts = await FetchSourceContextsAsync(sources, apiKey, jinaOptions, ct);
            var context = ExternalWebSearchResearchProvider.BuildContext(
                searchBody,
                sources,
                sourceContexts,
                request.MaxSources,
                request.IncludeCitations
            );

            context = ExternalWebSearchResearchProvider.Truncate(context, jinaOptions.MaxContentChars);

            return string.IsNullOrWhiteSpace(context)
                ? UnavailableResult()
                : Result<AiWebResearchBundle>.Success(new AiWebResearchBundle(context, sources));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return UnavailableResult();
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> FetchSourceContextsAsync(
        IReadOnlyList<AiWebResearchSource> sources,
        string? apiKey,
        JinaReaderOptions options,
        CancellationToken ct
    )
    {
        var contexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var uri)
                || !ExternalWebSearchResearchProvider.IsAllowedUrl(uri))
            {
                continue;
            }

            try
            {
                var proxiedUrl = $"https://r.jina.ai/http://{uri.Authority}{uri.PathAndQuery}";
                var body = await SendReaderRequestAsync(proxiedUrl, apiKey, options, ct);
                var snippet = ExternalWebSearchResearchProvider.ExtractSourceSnippet(body);
                if (!string.IsNullOrWhiteSpace(snippet))
                {
                    contexts[source.Url] = ExternalWebSearchResearchProvider.Truncate(snippet, options.MaxContentChars);
                }
            }
            catch
            {
                // Individual source fetches are best effort; the search result remains useful context.
            }
        }

        return contexts;
    }

    private async Task<string> SendReaderRequestAsync(
        string url,
        string? apiKey,
        JinaReaderOptions options,
        CancellationToken ct
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        request.Headers.TryAddWithoutValidation("X-Return-Format", options.ResponseFormat);
        if (options.UseReaderLmV2)
        {
            request.Headers.TryAddWithoutValidation("X-Engine", "browser");
        }

        if (options.RemoveImages)
        {
            request.Headers.TryAddWithoutValidation("X-Remove-Selector", "img");
        }

        if (!string.IsNullOrWhiteSpace(options.TargetSelector))
        {
            request.Headers.TryAddWithoutValidation("X-Target-Selector", options.TargetSelector);
        }

        if (!string.IsNullOrWhiteSpace(options.ExcludeSelector))
        {
            request.Headers.TryAddWithoutValidation("X-Remove-Selector", options.ExcludeSelector);
        }

        if (!string.IsNullOrWhiteSpace(options.WaitForSelector))
        {
            request.Headers.TryAddWithoutValidation("X-Wait-For-Selector", options.WaitForSelector);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        using var response = await httpClient.SendAsync(request, timeoutCts.Token);
        var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Jina Reader research endpoint was unavailable.");
        }

        if (IsUnavailableResponseBody(body))
        {
            throw new InvalidOperationException("Jina Reader research endpoint returned an unavailable response.");
        }

        return body;
    }

    private static Result<AiWebResearchBundle> UnavailableResult() =>
        Result<AiWebResearchBundle>.Success(new AiWebResearchBundle(ExternalResearchUnavailableNote, []));

    private static bool IsUnavailableResponseBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        return body.Contains("requested URL returned error", StringComparison.OrdinalIgnoreCase)
            || body.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase)
            || body.Contains("The requested content is not available", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string?> ResolveApiKeyAsync(
        EnrichmentSourceConfiguration source,
        CancellationToken ct
    )
    {
        if (source.StoredCredentialId is Guid storedCredentialId)
        {
            var credential = await dbContext.StoredCredentials.AsNoTracking()
                .FirstOrDefaultAsync(
                    item => item.Id == storedCredentialId && item.IsGlobal,
                    ct
                );

            if (credential is not null)
            {
                var storedApiKey = await secretStore.GetSecretAsync(credential.SecretRef, "apiKey", ct);
                if (!string.IsNullOrWhiteSpace(storedApiKey))
                {
                    return storedApiKey;
                }
            }
        }

        return string.IsNullOrWhiteSpace(source.SecretRef)
            ? null
            : await secretStore.GetSecretAsync(source.SecretRef, EnrichmentSourceCatalog.GetSecretKeyName(source.SourceKey), ct);
    }

    private static string BuildQuery(AiWebResearchRequest request)
    {
        if (request.AllowedDomains.Count == 0)
        {
            return request.Query;
        }

        var builder = new StringBuilder(request.Query);
        foreach (var domain in request.AllowedDomains)
        {
            builder.Append(" site:");
            builder.Append(domain);
        }

        return builder.ToString();
    }
}
