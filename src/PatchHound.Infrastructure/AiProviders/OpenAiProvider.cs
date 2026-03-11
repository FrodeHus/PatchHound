using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PatchHound.Core.Common;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;

namespace PatchHound.Infrastructure.AiProviders;

public class OpenAiProvider : IAiReportProvider
{
    private readonly HttpClient _httpClient;

    public OpenAiProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public TenantAiProviderType ProviderType => TenantAiProviderType.OpenAi;

    public Task<string> GenerateReportAsync(
        AiReportGenerationRequest request,
        TenantAiProfileResolved profile,
        CancellationToken ct
    ) => SendChatCompletionAsync(
        profile,
        profile.Profile.SystemPrompt,
        AiProviderPromptBuilder.BuildReportPrompt(request),
        profile.Profile.MaxOutputTokens,
        ct
    );

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
                max_tokens = maxTokens,
            }
        );

        using var response = await _httpClient.SendAsync(request, ct);
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
}
