using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Common;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;

namespace PatchHound.Infrastructure.AiProviders;

public class OpenAiProvider : IAiReportProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAiProvider> _logger;

    public OpenAiProvider(HttpClient httpClient, ILogger<OpenAiProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public TenantAiProviderType ProviderType => TenantAiProviderType.OpenAi;

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
    )
    {
        var userPrompt = BuildUserPrompt(request);
        var maxOutputTokens = request.MaxOutputTokens ?? profile.Profile.MaxOutputTokens;

        return request.UseProviderNativeWebResearch
            ? SendResponsesApiRequestAsync(
                profile,
                request.SystemPrompt,
                userPrompt,
                maxOutputTokens,
                request,
                ct
            )
            : SendChatCompletionAsync(
                profile,
                request.SystemPrompt,
                userPrompt,
                maxOutputTokens,
                ct
            );
    }

    public async Task<AiProviderValidationResult> ValidateAsync(
        TenantAiProfileResolved profile,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(profile.Profile.Model))
        {
            return AiProviderValidationResult.Failure("Model is required for OpenAI.");
        }

        if (string.IsNullOrWhiteSpace(profile.ApiKey))
        {
            return AiProviderValidationResult.Failure("API key is required for OpenAI.");
        }

        if (string.IsNullOrWhiteSpace(profile.Profile.BaseUrl))
        {
            return AiProviderValidationResult.Failure("Base URL is required for OpenAI.");
        }

        try
        {
            var content = await SendChatCompletionAsync(
                profile,
                "You are validating an OpenAI integration.",
                AiProviderPromptBuilder.BuildValidationPrompt(),
                16,
                ct
            );

            return string.IsNullOrWhiteSpace(content)
                ? AiProviderValidationResult.Failure("OpenAI validation returned an empty response.")
                : AiProviderValidationResult.Success();
        }
        catch (Exception ex)
        {
            return AiProviderValidationResult.Failure($"OpenAI validation failed: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<string>>> ListAvailableModelsAsync(
        TenantAiProfileResolved profile,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(profile.ApiKey))
        {
            return Result<IReadOnlyList<string>>.Failure("API key is required for OpenAI.");
        }

        if (string.IsNullOrWhiteSpace(profile.Profile.BaseUrl))
        {
            return Result<IReadOnlyList<string>>.Failure("Base URL is required for OpenAI.");
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{profile.Profile.BaseUrl.TrimEnd('/')}/models"
            );
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", profile.ApiKey);

            using var response = await _httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                return Result<IReadOnlyList<string>>.Failure(
                    AiProviderErrorParser.FormatHttpError(
                        "OpenAI",
                        (int)response.StatusCode,
                        response.ReasonPhrase,
                        body
                    )
                );
            }

            using var document = JsonDocument.Parse(body);
            var models = document.RootElement
                .GetProperty("data")
                .EnumerateArray()
                .Select(item => item.GetProperty("id").GetString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .OrderBy(item => item)
                .ToList();

            return Result<IReadOnlyList<string>>.Success(models);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<string>>.Failure($"OpenAI model listing failed: {ex.Message}");
        }
    }

    private async Task<string> SendChatCompletionAsync(
        TenantAiProfileResolved profile,
        string systemPrompt,
        string userPrompt,
        int maxTokens,
        CancellationToken ct
    )
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{profile.Profile.BaseUrl.TrimEnd('/')}/chat/completions"
        );
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", profile.ApiKey);
        request.Content = JsonContent.Create(
            new
            {
                model = profile.Profile.Model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt },
                },
                temperature = decimal.ToDouble(profile.Profile.Temperature),
                top_p = profile.Profile.TopP is decimal topP ? decimal.ToDouble(topP) : (double?)null,
                max_completion_tokens = maxTokens,
            }
        );

        using var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "OpenAI chat completion failed for model {Model} against {BaseUrl}. Status {StatusCode} {ReasonPhrase}. Response: {ResponseBody}",
                profile.Profile.Model,
                profile.Profile.BaseUrl,
                (int)response.StatusCode,
                response.ReasonPhrase,
                string.IsNullOrWhiteSpace(body) ? "<empty>" : body
            );
            throw new HttpRequestException(
                AiProviderErrorParser.FormatHttpError(
                    "OpenAI",
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
            throw new InvalidOperationException("OpenAI response did not contain message content.");
        }

        return content.Trim();
    }

    private async Task<string> SendResponsesApiRequestAsync(
        TenantAiProfileResolved profile,
        string systemPrompt,
        string userPrompt,
        int maxTokens,
        AiTextGenerationRequest request,
        CancellationToken ct
    )
    {
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{profile.Profile.BaseUrl.TrimEnd('/')}/responses"
        );
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", profile.ApiKey);
        httpRequest.Content = JsonContent.Create(
            new
            {
                model = profile.Profile.Model,
                input = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt },
                },
                tools = new object[]
                {
                    new
                    {
                        type = "web_search_preview",
                    },
                },
                temperature = decimal.ToDouble(profile.Profile.Temperature),
                top_p = profile.Profile.TopP is decimal topP ? decimal.ToDouble(topP) : (double?)null,
                max_output_tokens = maxTokens,
            }
        );

        using var response = await _httpClient.SendAsync(httpRequest, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                AiProviderErrorParser.FormatHttpError(
                    "OpenAI",
                    (int)response.StatusCode,
                    response.ReasonPhrase,
                    body
                )
            );
        }

        using var document = JsonDocument.Parse(body);
        if (document.RootElement.TryGetProperty("output_text", out var outputText)
            && outputText.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(outputText.GetString()))
        {
            return outputText.GetString()!.Trim();
        }

        if (document.RootElement.TryGetProperty("output", out var outputItems))
        {
            foreach (var item in outputItems.EnumerateArray())
            {
                if (!item.TryGetProperty("type", out var typeProperty)
                    || typeProperty.GetString() != "message"
                    || !item.TryGetProperty("content", out var contentItems))
                {
                    continue;
                }

                foreach (var contentItem in contentItems.EnumerateArray())
                {
                    if (contentItem.TryGetProperty("text", out var textProperty)
                        && textProperty.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(textProperty.GetString()))
                    {
                        return textProperty.GetString()!.Trim();
                    }
                }
            }
        }

        throw new InvalidOperationException("OpenAI responses API did not contain output text.");
    }

    private static string BuildUserPrompt(AiTextGenerationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ExternalContext))
        {
            return request.UserPrompt;
        }

        return $"{request.UserPrompt}\n\nExternal research context:\n{request.ExternalContext}";
    }
}
