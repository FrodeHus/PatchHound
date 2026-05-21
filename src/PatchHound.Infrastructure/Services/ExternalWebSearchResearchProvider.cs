using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using PatchHound.Core.Common;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Options;

namespace PatchHound.Infrastructure.Services;

public partial class ExternalWebSearchResearchProvider
{
    private readonly HttpClient _httpClient;
    private readonly AiResearchOptions _options;

    public ExternalWebSearchResearchProvider(
        HttpClient httpClient,
        IOptions<AiResearchOptions>? options = null
    )
    {
        _httpClient = httpClient;
        _options = options?.Value ?? new AiResearchOptions();
    }

    public async Task<Result<AiWebResearchBundle>> ResearchAsync(
        AiWebResearchRequest request,
        CancellationToken ct
    )
    {
        try
        {
            var query = BuildQuery(request);
            var url = BuildSearchUrl(query, _options.JinaSearchProvider);

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

    internal static string BuildSearchUrl(string query, string? provider)
    {
        var host = provider?.Trim().ToLowerInvariant() switch
        {
            "bing" => "www.bing.com",
            _ => "www.google.com",
        };

        return $"https://r.jina.ai/http://{host}/search?q={Uri.EscapeDataString(query)}";
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

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsAllowedUrl(uri))
            {
                continue;
            }

            var normalizedUrl = uri.ToString();
            if (!seen.Add(normalizedUrl))
            {
                continue;
            }

            results.Add(new AiWebResearchSource(title, normalizedUrl, null));
            if (results.Count >= maxSources)
            {
                break;
            }
        }

        if (results.Count >= maxSources)
        {
            return results;
        }

        foreach (Match match in BareUrlRegex().Matches(body))
        {
            var url = match.Value.TrimEnd('.', ',', ')', ']');
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsAllowedUrl(uri))
            {
                continue;
            }

            var normalizedUrl = uri.ToString();
            if (!seen.Add(normalizedUrl))
            {
                continue;
            }

            results.Add(new AiWebResearchSource(uri.Host, normalizedUrl, null));
            if (results.Count >= maxSources)
            {
                break;
            }
        }

        return results;
    }

    private async Task<IReadOnlyDictionary<string, string>> FetchSourceContextsAsync(
        IReadOnlyList<AiWebResearchSource> sources,
        CancellationToken ct
    )
    {
        if (sources.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        var contexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            try
            {
                if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var sourceUri))
                {
                    continue;
                }

                var proxiedUrl = $"https://r.jina.ai/http://{sourceUri.Host}{sourceUri.PathAndQuery}";
                using var response = await _httpClient.GetAsync(proxiedUrl, ct);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var body = await response.Content.ReadAsStringAsync(ct);
                var snippet = ExtractSourceSnippet(body);
                if (!string.IsNullOrWhiteSpace(snippet))
                {
                    contexts[source.Url] = snippet;
                }
            }
            catch
            {
                // Source fetches are best effort; the managed search response still carries context.
            }
        }

        return contexts;
    }

    private static bool IsAllowedUrl(Uri uri)
    {
        if (uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            if (
                IPAddress.TryParse(uri.Host, out var address)
                && (
                    IPAddress.IsLoopback(address)
                    || address.IsIPv6LinkLocal
                    || address.IsIPv6SiteLocal
                    || IsPrivateIpv4(address)
                )
            )
            {
                return false;
            }
        }

        return !uri.IsLoopback;
    }

    private static bool IsPrivateIpv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            || (bytes[0] == 192 && bytes[1] == 168);
    }

    private static string ExtractSourceSnippet(string body)
    {
        var text = body.Replace("\r", "\n");
        var lines = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("Title:", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.StartsWith("URL Source:", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.StartsWith("Markdown Content:", StringComparison.OrdinalIgnoreCase))
            .Take(40);

        return string.Join('\n', lines);
    }

    private static string BuildContext(
        string searchBody,
        IReadOnlyList<AiWebResearchSource> sources,
        IReadOnlyDictionary<string, string> sourceContexts,
        int maxSources,
        bool includeCitations
    )
    {
        var builder = new StringBuilder();
        builder.AppendLine("External research context:");
        builder.AppendLine(ExtractSourceSnippet(searchBody));

        if (sourceContexts.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Fetched source context:");
            foreach (var source in sources.Take(maxSources))
            {
                if (!sourceContexts.TryGetValue(source.Url, out var snippet))
                {
                    continue;
                }

                builder.AppendLine($"- {source.Title} ({source.Url})");
                builder.AppendLine(snippet);
            }
        }

        if (includeCitations && sources.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Sources:");
            foreach (var source in sources.Take(maxSources))
            {
                builder.AppendLine($"- {source.Title}: {source.Url}");
            }
        }

        return builder.ToString().Trim();
    }

    [GeneratedRegex(@"\[(?<title>[^\]]+)\]\((?<url>https?://[^)]+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"https?://[^\s\])>]+", RegexOptions.IgnoreCase)]
    private static partial Regex BareUrlRegex();
}
