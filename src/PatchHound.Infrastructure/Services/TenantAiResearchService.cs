using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using PatchHound.Core.Common;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;

namespace PatchHound.Infrastructure.Services;

public partial class TenantAiResearchService : ITenantAiResearchService
{
    private readonly HttpClient _httpClient;

    public TenantAiResearchService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<Result<AiWebResearchBundle>> ResearchAsync(
        TenantAiProfileResolved profile,
        AiWebResearchRequest request,
        CancellationToken ct
    )
    {
        return ResearchInternalAsync(profile, request, ct);
    }

    private async Task<Result<AiWebResearchBundle>> ResearchInternalAsync(
        TenantAiProfileResolved profile,
        AiWebResearchRequest request,
        CancellationToken ct
    )
    {
        try
        {
            var query = BuildQuery(request);
            var url =
                $"https://r.jina.ai/http://www.bing.com/search?q={Uri.EscapeDataString(query)}";

            using var response = await _httpClient.GetAsync(url, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return Result<AiWebResearchBundle>.Failure(
                    $"PatchHound-managed web research failed: {(int)response.StatusCode} {response.ReasonPhrase}"
                );
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                return Result<AiWebResearchBundle>.Failure(
                    "PatchHound-managed web research returned an empty response."
                );
            }

            var sources = ExtractSources(body, request.MaxSources);
            var sourceContexts = await FetchSourceContextsAsync(sources, ct);
            var context = BuildContext(
                body,
                sources,
                sourceContexts,
                request.MaxSources,
                request.IncludeCitations
            );

            if (string.IsNullOrWhiteSpace(context))
            {
                return Result<AiWebResearchBundle>.Failure(
                    "PatchHound-managed web research did not return usable context."
                );
            }

            return Result<AiWebResearchBundle>.Success(
                new AiWebResearchBundle(context, sources)
            );
        }
        catch (Exception ex)
        {
            return Result<AiWebResearchBundle>.Failure(
                $"PatchHound-managed web research failed: {ex.Message}"
            );
        }
    }

    private static string BuildQuery(AiWebResearchRequest request)
    {
        if (request.AllowedDomains.Count == 0)
        {
            return request.Query;
        }

        var domainTerms = request.AllowedDomains.Select(domain => $"site:{domain}");
        return $"{request.Query} {string.Join(" OR ", domainTerms)}";
    }

    private static IReadOnlyList<AiWebResearchSource> ExtractSources(string body, int maxSources)
    {
        var results = new List<AiWebResearchSource>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in MarkdownLinkRegex().Matches(body))
        {
            var title = WebUtility.HtmlDecode(match.Groups["title"].Value.Trim());
            var url = match.Groups["url"].Value.Trim();
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            if (!seen.Add(url))
            {
                continue;
            }

            results.Add(new AiWebResearchSource(title, url, null));
            if (results.Count >= maxSources)
            {
                break;
            }
        }

        foreach (Match match in BareUrlRegex().Matches(body))
        {
            var url = match.Value.Trim();
            if (!seen.Add(url))
            {
                continue;
            }

            results.Add(new AiWebResearchSource(url, url, null));
            if (results.Count >= maxSources)
            {
                break;
            }
        }

        return results;
    }

    private async Task<IReadOnlyList<string>> FetchSourceContextsAsync(
        IReadOnlyList<AiWebResearchSource> sources,
        CancellationToken ct
    )
    {
        var contexts = new List<string>();

        foreach (var source in sources)
        {
            try
            {
                using var response = await _httpClient.GetAsync($"https://r.jina.ai/http://{source.Url.Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)}", ct);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var body = await response.Content.ReadAsStringAsync(ct);
                if (string.IsNullOrWhiteSpace(body))
                {
                    continue;
                }

                var snippet = ExtractSourceSnippet(body);
                if (!string.IsNullOrWhiteSpace(snippet))
                {
                    contexts.Add($"Source: {source.Title}\n{snippet}");
                }
            }
            catch
            {
                // Best-effort enrichment only; continue with available source context.
            }
        }

        return contexts;
    }

    private static string? ExtractSourceSnippet(string body)
    {
        var normalizedBody = body
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        var lines = normalizedBody
            .Split('\n', StringSplitOptions.TrimEntries)
            .Where(line =>
                !string.IsNullOrWhiteSpace(line)
                && !line.StartsWith("Title:", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("URL Source:", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("Markdown Content:", StringComparison.OrdinalIgnoreCase)
            )
            .Take(18)
            .ToList();

        if (lines.Count == 0)
        {
            return null;
        }

        var snippet = string.Join('\n', lines);
        return snippet.Length > 1800 ? snippet[..1800] : snippet;
    }

    private static string BuildContext(
        string body,
        IReadOnlyList<AiWebResearchSource> sources,
        IReadOnlyList<string> sourceContexts,
        int maxSources,
        bool includeCitations
    )
    {
        var normalizedBody = body
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        var lines = normalizedBody
            .Split('\n', StringSplitOptions.TrimEntries)
            .Where(line =>
                !string.IsNullOrWhiteSpace(line)
                && !line.StartsWith("Title:", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("URL Source:", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("Markdown Content:", StringComparison.OrdinalIgnoreCase)
            )
            .Take(40)
            .ToList();

        var builder = new StringBuilder();
        builder.AppendLine("External research context:");
        foreach (var line in lines)
        {
            builder.AppendLine(line);
        }

        if (sourceContexts.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Fetched source context:");
            foreach (var sourceContext in sourceContexts.Take(maxSources))
            {
                builder.AppendLine(sourceContext);
                builder.AppendLine();
            }
        }

        if (includeCitations && sources.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Sources:");
            foreach (var source in sources.Take(maxSources))
            {
                builder.Append("- ");
                builder.Append(source.Title);
                builder.Append(" — ");
                builder.AppendLine(source.Url);
            }
        }

        var context = builder.ToString().Trim();
        return context.Length > 6000 ? context[..6000] : context;
    }

    [GeneratedRegex(@"\[(?<title>[^\]]+)\]\((?<url>https?://[^)\s]+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"https?://[^\s)>\]]+", RegexOptions.IgnoreCase)]
    private static partial Regex BareUrlRegex();
}
