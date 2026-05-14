using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PatchHound.Core.Common;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;

namespace PatchHound.Infrastructure.AiProviders;

public class AzureOpenAiProvider : IAiReportProvider
{
    private readonly HttpClient _httpClient;

    public AzureOpenAiProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public TenantAiProviderType ProviderType => TenantAiProviderType.AzureOpenAi;

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
    ) => SendChatCompletionAsync(
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
            return AiProviderValidationResult.Failure("Endpoint is required for Azure OpenAI.");
        }

        if (string.IsNullOrWhiteSpace(profile.Profile.DeploymentName))
        {
            return AiProviderValidationResult.Failure("Deployment name is required for Azure OpenAI.");
        }

        if (string.IsNullOrWhiteSpace(profile.Profile.ApiVersion))
        {
            return AiProviderValidationResult.Failure("API version is required for Azure OpenAI.");
        }

        if (string.IsNullOrWhiteSpace(profile.ApiKey))
        {
            return AiProviderValidationResult.Failure("API key is required for Azure OpenAI.");
        }

        try
        {
            var content = await SendChatCompletionAsync(
                profile,
                "You are validating an Azure OpenAI integration.",
                AiProviderPromptBuilder.BuildValidationPrompt(),
                16,
                ct
            );

            return string.IsNullOrWhiteSpace(content)
                ? AiProviderValidationResult.Failure("Azure OpenAI validation returned an empty response.")
                : AiProviderValidationResult.Success();
        }
        catch (Exception ex)
        {
            return AiProviderValidationResult.Failure($"Azure OpenAI validation failed: {ex.Message}");
        }
    }

    public Task<Result<IReadOnlyList<string>>> ListAvailableModelsAsync(
        TenantAiProfileResolved profile,
        CancellationToken ct
    )
    {
        return Task.FromResult(
            Result<IReadOnlyList<string>>.Failure(
                "Azure OpenAI model discovery is not supported here. Enter the deployment name configured in Azure."
            )
        );
    }

    private async Task<string> SendChatCompletionAsync(
        TenantAiProfileResolved profile,
        string systemPrompt,
        string userPrompt,
        int maxTokens,
        CancellationToken ct
    )
    {
        var endpoint =
            $"{profile.Profile.BaseUrl.TrimEnd('/')}/openai/deployments/{profile.Profile.DeploymentName}/chat/completions?api-version={Uri.EscapeDataString(profile.Profile.ApiVersion)}";

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("api-key", profile.ApiKey);
        request.Content = JsonContent.Create(
            new
            {
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
                    "Azure OpenAI",
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
            throw new InvalidOperationException("Azure OpenAI response did not contain message content.");
        }

        return content.Trim();
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
