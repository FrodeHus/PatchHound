using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.EnrichmentChanges;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/enrichment-changes")]
[Authorize]
public class EnrichmentChangesController : ControllerBase
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public EnrichmentChangesController(PatchHoundDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<PagedResponse<EnrichmentChangeDto>>> List(
        [FromQuery] EnrichmentChangeFilterQuery filter,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        if (_tenantContext.CurrentTenantId is not Guid currentTenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        if (string.IsNullOrWhiteSpace(filter.EntityType))
            return BadRequest(new ProblemDetails { Title = "Entity type is required." });

        if (filter.EntityId == Guid.Empty)
            return BadRequest(new ProblemDetails { Title = "Entity id is required." });

        var entityType = filter.EntityType.Trim();
        var query = _dbContext.EnrichmentChangeLogs.AsNoTracking()
            .Where(change =>
                change.EntityType == entityType
                && change.EntityId == filter.EntityId
                && (
                    (change.Scope == EnrichmentChangeScope.Global && change.TenantId == null)
                    || (
                        change.Scope == EnrichmentChangeScope.Tenant
                        && change.TenantId == currentTenantId
                    )
                )
            );

        if (!string.IsNullOrWhiteSpace(filter.SourceKey))
        {
            var sourceKey = filter.SourceKey.Trim().ToLowerInvariant();
            query = query.Where(change => change.SourceKey == sourceKey);
        }

        if (!string.IsNullOrWhiteSpace(filter.FieldPath))
        {
            var fieldPath = filter.FieldPath.Trim();
            query = query.Where(change => change.FieldPath == fieldPath);
        }

        if (filter.FromDate.HasValue)
            query = query.Where(change => change.ChangedAt >= filter.FromDate.Value);

        if (filter.ToDate.HasValue)
            query = query.Where(change => change.ChangedAt <= filter.ToDate.Value);

        var totalCount = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(change => change.ChangedAt)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .ToListAsync(ct);

        var items = rows
            .Select(change => new EnrichmentChangeDto(
                change.Id,
                change.Scope.ToString(),
                change.TenantId,
                change.EntityType,
                change.EntityId,
                change.SourceKey,
                ResolveSourceDisplayName(change.SourceKey),
                change.FieldPath,
                change.DisplayName,
                ParseJsonValue(change.OldValueJson),
                ParseJsonValue(change.NewValueJson),
                change.ValueKind.ToString(),
                change.ChangedAt,
                change.EnrichmentRunId,
                change.EnrichmentJobId,
                change.ChangeReason,
                change.Confidence
            ))
            .ToList();

        return Ok(
            new PagedResponse<EnrichmentChangeDto>(
                items,
                totalCount,
                pagination.Page,
                pagination.BoundedPageSize
            )
        );
    }

    private static object? ParseJsonValue(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            return ConvertElement(document.RootElement);
        }
        catch (JsonException)
        {
            return raw;
        }
    }

    private static object? ConvertElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetDecimal(out var value)
                ? value
                : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Object => element
                .EnumerateObject()
                .ToDictionary(
                    property => property.Name,
                    property => ConvertElement(property.Value)
                ),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertElement).ToList(),
            _ => element.ToString(),
        };
    }

    private static string? ResolveSourceDisplayName(string sourceKey)
    {
        return sourceKey.ToLowerInvariant() switch
        {
            EnrichmentSourceCatalog.DefenderSourceKey => "Microsoft Defender",
            EnrichmentSourceCatalog.NvdSourceKey => "NVD API",
            EnrichmentSourceCatalog.EndOfLifeSourceKey => "Software End of Life",
            EnrichmentSourceCatalog.SupplyChainSourceKey => "Supply Chain Evidence",
            _ => null,
        };
    }
}
