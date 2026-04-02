using PatchHound.Core.Enums;

namespace PatchHound.Api.Models.AdvancedTools;

public record AdvancedToolDto(
    Guid Id,
    string Name,
    string Description,
    IReadOnlyList<string> SupportedAssetTypes,
    string KqlQuery,
    string AiPrompt,
    bool Enabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record SaveAdvancedToolRequest(
    string Name,
    string Description,
    IReadOnlyList<string> SupportedAssetTypes,
    string KqlQuery,
    string? AiPrompt,
    bool Enabled
);

public record AdvancedToolTestRequest(
    string KqlQuery,
    IReadOnlyDictionary<string, string?> SampleParameters
);

public record AdvancedToolAiSummaryTestRequest(
    string KqlQuery,
    string? AiPrompt,
    IReadOnlyDictionary<string, string?> SampleParameters
);

public record RunAdvancedToolForAssetRequest(
    Guid? ToolId,
    string? KqlQuery,
    bool UseAllOpenVulnerabilities,
    IReadOnlyList<Guid>? VulnerabilityIds
);

public record AdvancedToolParameterDefinitionDto(
    string Name,
    string Description
);

public record AdvancedToolSchemaColumnDto(
    string Name,
    string Type
);

public record AdvancedToolExecutionResultDto(
    IReadOnlyList<AdvancedToolSchemaColumnDto> Schema,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Results,
    string RenderedQuery
);

public record AdvancedToolAiSummaryResultDto(
    string RenderedQuery,
    string Content,
    string ProfileName,
    string ProviderType,
    string Model,
    DateTimeOffset GeneratedAt
);

public record AdvancedToolAssetAiReportDto(
    string Content,
    string ProfileName,
    string ProviderType,
    string Model,
    DateTimeOffset GeneratedAt
);

public record AdvancedToolMergedResultDto(
    IReadOnlyList<AdvancedToolSchemaColumnDto> Schema,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    int RowCount
);

public record AdvancedToolAssetExecutionResultDto(
    AdvancedToolMergedResultDto RawResults,
    AdvancedToolAssetAiReportDto? Report,
    string? AiUnavailableMessage
);

public record AdvancedToolCatalogDto(
    IReadOnlyList<AdvancedToolDto> Tools,
    IReadOnlyList<AdvancedToolParameterDefinitionDto> AvailableParameters
);
