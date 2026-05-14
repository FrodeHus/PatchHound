using System.Net.Http.Json;
using System.Text.Json;
using PatchHound.Core.Common;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;

namespace PatchHound.Infrastructure.AiProviders;

public class OllamaAiProvider : IAiReportProvider
{
    private readonly HttpClient _httpClient;

    public OllamaAiProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public TenantAiProviderType ProviderType => TenantAiProviderType.Ollama;

    public Task<string> GenerateReportAsync(
        AiReportGenerationRequest request,
        TenantAiProfileResolved profile,
        CancellationToken ct
    ) => GenerateTextAsync(
        new AiTextGenerationRequest(
            profile.Profile.SystemPrompt,
            AiProviderPromptBuilder.BuildReportPrompt(request)
        ),
        profile,
        ct
    );

    public Task<string> GenerateTextAsync(
        AiTextGenerationRequest request,
        TenantAiProfileResolved profile,
        CancellationToken ct
    ) => SendGenerateAsync(
        profile,
        request.SystemPrompt,
        BuildUserPrompt(request),
        request.MaxOutputTokens ?? profile.Profile.MaxOutputTokens,
        ct
    );

    public async Task<AiProviderValidationResult> ValidateAsync(
        TenantAiProfileResolved profile,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(profile.Profile.BaseUrl))
        {
            return AiProviderValidationResult.Failure("Base URL is required for Ollama.");
        }

        if (string.IsNullOrWhiteSpace(profile.Profile.Model))
        {
            return AiProviderValidationResult.Failure("Model is required for Ollama.");
        }

        try
        {
            var content = await SendGenerateAsync(
                profile,
                "You are validating an Ollama integration.",
                AiProviderPromptBuilder.BuildValidationPrompt(),
                16,
                ct
            );

            return string.IsNullOrWhiteSpace(content)
                ? AiProviderValidationResult.Failure("Ollama validation returned an empty response.")
                : AiProviderValidationResult.Success();
        }
        catch (Exception ex)
        {
            return AiProviderValidationResult.Failure($"Ollama validation failed: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<string>>> ListAvailableModelsAsync(
        TenantAiProfileResolved profile,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(profile.Profile.BaseUrl))
        {
            return Result<IReadOnlyList<string>>.Failure("Base URL is required for Ollama.");
        }

        try
        {
            using var response = await _httpClient.GetAsync(
                $"{NormalizeBaseUrl(profile.Profile.BaseUrl)}/tags",
                ct
            );
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                return Result<IReadOnlyList<string>>.Failure(
                    AiProviderErrorParser.FormatHttpError(
                        "Ollama",
                        (int)response.StatusCode,
                        response.ReasonPhrase,
                        body
                    )
                );
            }

            using var document = JsonDocument.Parse(body);
            var models = document.RootElement
                .GetProperty("models")
                .EnumerateArray()
                .Select(item => item.GetProperty("name").GetString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .OrderBy(item => item)
                .ToList();

            return Result<IReadOnlyList<string>>.Success(models);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<string>>.Failure($"Ollama model listing failed: {ex.Message}");
        }
    }

    private async Task<string> SendGenerateAsync(
        TenantAiProfileResolved profile,
        string systemPrompt,
        string prompt,
        int maxTokens,
        CancellationToken ct
    )
    {
        var nativeResponse = await SendNativeGenerateAsync(profile, systemPrompt, prompt, maxTokens, ct);
        if (nativeResponse.IsSuccess)
        {
            return nativeResponse.Content;
        }

        if (nativeResponse.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            throw new HttpRequestException(nativeResponse.ErrorMessage);
        }

        var openAiResponse = await SendOpenAiCompatibleChatAsync(profile, systemPrompt, prompt, maxTokens, ct);
        if (openAiResponse.IsSuccess)
        {
            return openAiResponse.Content;
        }

        throw new HttpRequestException(openAiResponse.ErrorMessage);
    }

    private async Task<ProviderCallResult> SendNativeGenerateAsync(
        TenantAiProfileResolved profile,
        string systemPrompt,
        string prompt,
        int maxTokens,
        CancellationToken ct
    )
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{NormalizeBaseUrl(profile.Profile.BaseUrl)}/generate"
        );
        request.Content = JsonContent.Create(
            new
            {
                model = profile.Profile.Model,
                stream = false,
                keep_alive = string.IsNullOrWhiteSpace(profile.Profile.KeepAlive)
                    ? null
                    : profile.Profile.KeepAlive,
                system = systemPrompt,
                prompt,
                options = new
                {
                    temperature = decimal.ToDouble(profile.Profile.Temperature),
                    top_p = profile.Profile.TopP is decimal topP ? decimal.ToDouble(topP) : (double?)null,
                    num_predict = maxTokens,
                },
            }
        );

        using var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            return ProviderCallResult.Failure(
                response.StatusCode,
                AiProviderErrorParser.FormatHttpError(
                    "Ollama",
                    (int)response.StatusCode,
                    response.ReasonPhrase,
                    body
                )
            );
        }

        using var document = JsonDocument.Parse(body);
        var content = document.RootElement.GetProperty("response").GetString();

        if (string.IsNullOrWhiteSpace(content))
        {
            return ProviderCallResult.Failure(
                null,
                "Ollama response did not contain generated content."
            );
        }

        return ProviderCallResult.Success(content.Trim());
    }

    private async Task<ProviderCallResult> SendOpenAiCompatibleChatAsync(
        TenantAiProfileResolved profile,
        string systemPrompt,
        string prompt,
        int maxTokens,
        CancellationToken ct
    )
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{NormalizeOpenAiBaseUrl(profile.Profile.BaseUrl)}/chat/completions"
        );
        request.Content = JsonContent.Create(
            new
            {
                model = profile.Profile.Model,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = prompt },
                },
                stream = false,
                temperature = decimal.ToDouble(profile.Profile.Temperature),
                top_p = profile.Profile.TopP is decimal topP ? decimal.ToDouble(topP) : (double?)null,
                max_tokens = maxTokens,
            }
        );

        using var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            return ProviderCallResult.Failure(
                response.StatusCode,
                AiProviderErrorParser.FormatHttpError(
                    "Ollama",
                    (int)response.StatusCode,
                    response.ReasonPhrase,
                    body
                )
            );
        }

        using var document = JsonDocument.Parse(body);
        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(content))
        {
            return ProviderCallResult.Failure(
                null,
                "Ollama OpenAI-compatible response did not contain message content."
            );
        }

        return ProviderCallResult.Success(content.Trim());
    }
    private static string NormalizeBaseUrl(string baseUrl)
    {
        var normalized = baseUrl.Trim().TrimEnd('/');

        normalized = StripKnownSuffix(normalized, "/api/generate");
        normalized = StripKnownSuffix(normalized, "/api/chat");
        normalized = StripKnownSuffix(normalized, "/v1/chat/completions");

        return normalized.EndsWith("/api", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : $"{normalized}/api";
    }

    private static string BuildUserPrompt(AiTextGenerationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ExternalContext))
        {
            return request.UserPrompt;
        }

        return $"{request.UserPrompt}\n\nExternal research context:\n{request.ExternalContext}";
    }

    private static string NormalizeOpenAiBaseUrl(string baseUrl)
    {
        var normalized = baseUrl.Trim().TrimEnd('/');
        normalized = StripKnownSuffix(normalized, "/api/generate");
        normalized = StripKnownSuffix(normalized, "/api/chat");
        normalized = StripKnownSuffix(normalized, "/v1/chat/completions");

        if (normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        if (normalized.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        return $"{normalized}/v1";
    }

    private sealed record ProviderCallResult(
        bool IsSuccess,
        string Content,
        string ErrorMessage,
        System.Net.HttpStatusCode? StatusCode
    )
    {
        public static ProviderCallResult Success(string content) => new(true, content, string.Empty, null);

        public static ProviderCallResult Failure(System.Net.HttpStatusCode? statusCode, string errorMessage) =>
            new(false, string.Empty, errorMessage, statusCode);
    }

    private static string StripKnownSuffix(string value, string suffix) =>
        value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? value[..^suffix.Length]
            : value;
}
