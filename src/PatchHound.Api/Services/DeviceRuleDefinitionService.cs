using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Enums;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Services;

public sealed class DeviceRuleDefinitionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly PatchHoundDbContext _dbContext;

    public DeviceRuleDefinitionService(PatchHoundDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<DeviceRuleDefinitionResult> ParseAndValidateAsync(
        Guid tenantId,
        string assetType,
        JsonElement filterDefinition,
        JsonElement operationsElement,
        CancellationToken ct)
    {
        if (!DeviceRuleAssetTypes.IsSupported(assetType))
            return DeviceRuleDefinitionResult.Failure("Unsupported asset type.");

        var filterResult = ParseFilter(filterDefinition);
        if (!filterResult.IsSuccess)
            return DeviceRuleDefinitionResult.Failure(filterResult.Error!);

        var operationsResult = ParseOperations(operationsElement);
        if (!operationsResult.IsSuccess)
            return DeviceRuleDefinitionResult.Failure(operationsResult.Error!);

        var operations = operationsResult.Operations;
        var validationError = ValidateOperations(assetType, operations);
        if (validationError is not null)
            return DeviceRuleDefinitionResult.Failure(validationError);

        var referenceValidationError = await ValidateOperationReferencesAsync(tenantId, operations, ct);
        if (referenceValidationError is not null)
            return DeviceRuleDefinitionResult.Failure(referenceValidationError);

        return DeviceRuleDefinitionResult.Success(filterResult.Filter!, operations);
    }

    public DeviceRuleFilterParseResult ParseFilter(JsonElement element)
    {
        try
        {
            var filter = JsonSerializer.Deserialize<FilterNode>(element.GetRawText(), JsonOptions);
            return filter is null
                ? DeviceRuleFilterParseResult.Failure("Invalid filter JSON.")
                : DeviceRuleFilterParseResult.Success(filter);
        }
        catch (JsonException ex)
        {
            return DeviceRuleFilterParseResult.Failure($"Invalid filter JSON: {ex.Message}");
        }
        catch (NotSupportedException ex)
        {
            return DeviceRuleFilterParseResult.Failure($"Invalid filter JSON: {ex.Message}");
        }
    }

    private static DeviceRuleOperationsParseResult ParseOperations(JsonElement element)
    {
        try
        {
            var operations = JsonSerializer.Deserialize<List<AssetRuleOperation>>(element.GetRawText(), JsonOptions);
            return operations is null
                ? DeviceRuleOperationsParseResult.Failure("Invalid operations JSON.")
                : DeviceRuleOperationsParseResult.Success(operations);
        }
        catch (JsonException ex)
        {
            return DeviceRuleOperationsParseResult.Failure($"Invalid operations JSON: {ex.Message}");
        }
        catch (NotSupportedException ex)
        {
            return DeviceRuleOperationsParseResult.Failure($"Invalid operations JSON: {ex.Message}");
        }
    }

    private static string? ValidateOperations(string assetType, IReadOnlyList<AssetRuleOperation> operations)
    {
        if (DeviceRuleAssetTypes.IsSoftware(assetType) || DeviceRuleAssetTypes.IsApplication(assetType))
        {
            foreach (var operation in operations)
            {
                switch (operation.Type)
                {
                    case DeviceRuleOperationTypes.AssignOwnerTeam:
                        if (!operation.Parameters.TryGetValue("teamId", out var softwareTeamId)
                            || !Guid.TryParse(softwareTeamId, out _))
                        {
                            return "AssignOwnerTeam requires a valid teamId.";
                        }
                        break;
                    default:
                        return DeviceRuleAssetTypes.IsSoftware(assetType)
                            ? $"Unknown software rule operation type: {operation.Type}."
                            : $"Unknown application rule operation type: {operation.Type}.";
                }
            }

            return null;
        }

        foreach (var operation in operations)
        {
            switch (operation.Type)
            {
                case DeviceRuleOperationTypes.AssignSecurityProfile:
                    if (!operation.Parameters.TryGetValue("securityProfileId", out var securityProfileId)
                        || !Guid.TryParse(securityProfileId, out _))
                    {
                        return "AssignSecurityProfile requires a valid securityProfileId.";
                    }
                    break;

                case DeviceRuleOperationTypes.AssignTeam:
                    if (!operation.Parameters.TryGetValue("teamId", out var teamId)
                        || !Guid.TryParse(teamId, out _))
                    {
                        return "AssignTeam requires a valid teamId.";
                    }
                    break;

                case DeviceRuleOperationTypes.AssignOwnerTeam:
                    if (!operation.Parameters.TryGetValue("teamId", out var ownerTeamId)
                        || !Guid.TryParse(ownerTeamId, out _))
                    {
                        return "AssignOwnerTeam requires a valid teamId.";
                    }
                    break;

                case DeviceRuleOperationTypes.AssignBusinessLabel:
                    if (!operation.Parameters.TryGetValue("businessLabelId", out var businessLabelId)
                        || !Guid.TryParse(businessLabelId, out _))
                    {
                        return "AssignBusinessLabel requires a valid businessLabelId.";
                    }
                    break;

                case DeviceRuleOperationTypes.SetCriticality:
                    if (!operation.Parameters.TryGetValue("criticality", out var criticality)
                        || !Enum.TryParse<Criticality>(criticality, true, out _))
                    {
                        return "SetCriticality requires a valid criticality value.";
                    }
                    break;

                default:
                    return $"Unknown device rule operation type: {operation.Type}.";
            }
        }

        return null;
    }

    private async Task<string?> ValidateOperationReferencesAsync(
        Guid tenantId,
        IReadOnlyList<AssetRuleOperation> operations,
        CancellationToken ct)
    {
        foreach (var operation in operations)
        {
            switch (operation.Type)
            {
                case DeviceRuleOperationTypes.AssignSecurityProfile:
                    if (operation.Parameters.TryGetValue("securityProfileId", out var securityProfileId)
                        && Guid.TryParse(securityProfileId, out var parsedSecurityProfileId))
                    {
                        var exists = await _dbContext.SecurityProfiles
                            .AnyAsync(profile => profile.TenantId == tenantId && profile.Id == parsedSecurityProfileId, ct);
                        if (!exists)
                            return "AssignSecurityProfile references a security profile that does not belong to the active tenant.";
                    }
                    break;

                case DeviceRuleOperationTypes.AssignTeam:
                case DeviceRuleOperationTypes.AssignOwnerTeam:
                    if (operation.Parameters.TryGetValue("teamId", out var teamId)
                        && Guid.TryParse(teamId, out var parsedTeamId))
                    {
                        var exists = await _dbContext.Teams
                            .AnyAsync(team => team.TenantId == tenantId && team.Id == parsedTeamId, ct);
                        if (!exists)
                            return $"{operation.Type} references a team that does not belong to the active tenant.";
                    }
                    break;

                case DeviceRuleOperationTypes.AssignBusinessLabel:
                    if (operation.Parameters.TryGetValue("businessLabelId", out var businessLabelId)
                        && Guid.TryParse(businessLabelId, out var parsedBusinessLabelId))
                    {
                        var exists = await _dbContext.BusinessLabels
                            .AnyAsync(label => label.TenantId == tenantId && label.IsActive && label.Id == parsedBusinessLabelId, ct);
                        if (!exists)
                            return "AssignBusinessLabel references an active business label that does not belong to the active tenant.";
                    }
                    break;
            }
        }

        return null;
    }
}

public sealed record DeviceRuleDefinitionResult(
    bool IsSuccess,
    FilterNode? Filter,
    List<AssetRuleOperation> Operations,
    string? Error)
{
    public static DeviceRuleDefinitionResult Success(FilterNode filter, List<AssetRuleOperation> operations) =>
        new(true, filter, operations, null);

    public static DeviceRuleDefinitionResult Failure(string error) =>
        new(false, null, [], error);
}

public sealed record DeviceRuleFilterParseResult(bool IsSuccess, FilterNode? Filter, string? Error)
{
    public static DeviceRuleFilterParseResult Success(FilterNode filter) => new(true, filter, null);

    public static DeviceRuleFilterParseResult Failure(string error) => new(false, null, error);
}

internal sealed record DeviceRuleOperationsParseResult(
    bool IsSuccess,
    List<AssetRuleOperation> Operations,
    string? Error)
{
    public static DeviceRuleOperationsParseResult Success(List<AssetRuleOperation> operations) =>
        new(true, operations, null);

    public static DeviceRuleOperationsParseResult Failure(string error) =>
        new(false, [], error);
}
