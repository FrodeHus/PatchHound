namespace PatchHound.Infrastructure.Services;

public record DefenderAdvancedQuerySchemaColumn(
    string Name,
    string Type
);

public record DefenderAdvancedQueryResult(
    IReadOnlyList<DefenderAdvancedQuerySchemaColumn> Schema,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Results
);

public record AdvancedToolVulnerabilityContext(
    Guid VulnerabilityId,
    string ExternalId,
    string? Vendor,
    string? Product,
    string? Version
);

public record AdvancedToolExecutionContext(
    Guid TenantId,
    Guid AssetId,
    string AssetType,
    string? DeviceExternalId,
    string? DeviceName,
    IReadOnlyList<AdvancedToolVulnerabilityContext> Vulnerabilities
);
