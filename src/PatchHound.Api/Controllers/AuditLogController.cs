using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Audit;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/audit-log")]
[Authorize]
public class AuditLogController : ControllerBase
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public AuditLogController(PatchHoundDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [Authorize(Policy = Policies.ViewAuditLogs)]
    public async Task<ActionResult<PagedResponse<AuditLogDto>>> List(
        [FromQuery] AuditLogFilterQuery filter,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        var query = _dbContext.AuditLogEntries.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(filter.EntityType))
            query = query.Where(e => e.EntityType == filter.EntityType);
        if (filter.EntityId.HasValue)
            query = query.Where(e => e.EntityId == filter.EntityId.Value);
        if (filter.Action.HasValue)
            query = query.Where(e => e.Action == filter.Action.Value);
        if (filter.UserId.HasValue)
            query = query.Where(e => e.UserId == filter.UserId.Value);
        if (filter.TenantId.HasValue)
        {
            if (!_tenantContext.HasAccessToTenant(filter.TenantId.Value))
                return Forbid();
            query = query.Where(e => e.TenantId == filter.TenantId.Value);
        }
        if (filter.FromDate.HasValue)
            query = query.Where(e => e.Timestamp >= filter.FromDate.Value);
        if (filter.ToDate.HasValue)
            query = query.Where(e => e.Timestamp <= filter.ToDate.Value);

        var totalCount = await query.CountAsync(ct);

        var entries = await query
            .OrderByDescending(e => e.Timestamp)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .ToListAsync(ct);

        var userDisplayNames = await _dbContext
            .Users.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(user => entries.Select(entry => entry.UserId).Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.DisplayName, ct);

        var items = entries
            .Select(entry => new AuditLogDto(
                entry.Id,
                entry.TenantId,
                entry.EntityType,
                entry.EntityId,
                ResolveEntityLabel(entry.EntityType, entry.OldValues, entry.NewValues),
                entry.Action.ToString(),
                entry.OldValues,
                entry.NewValues,
                entry.UserId,
                userDisplayNames.GetValueOrDefault(entry.UserId),
                entry.Timestamp
            ))
            .ToList();

        return Ok(new PagedResponse<AuditLogDto>(items, totalCount, pagination.Page, pagination.BoundedPageSize));
    }

    private static string? ResolveEntityLabel(string entityType, string? oldValues, string? newValues)
    {
        var values = ParseValues(newValues).Count > 0 ? ParseValues(newValues) : ParseValues(oldValues);

        return entityType switch
        {
            "Tenant" => GetValue(values, "Name"),
            "TenantSourceConfiguration" => GetValue(values, "DisplayName") ?? GetValue(values, "SourceKey"),
            "EnrichmentSourceConfiguration" => GetValue(values, "DisplayName") ?? GetValue(values, "SourceKey"),
            "AssetSecurityProfile" => GetValue(values, "Name"),
            "Team" => GetValue(values, "Name"),
            "UserTenantRole" => GetValue(values, "Role"),
            _ => GetValue(values, "Name") ?? GetValue(values, "DisplayName") ?? GetValue(values, "Title"),
        };
    }

    private static Dictionary<string, string?> ParseValues(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                values[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Null => null,
                    _ => property.Value.ToString(),
                };
            }

            return values;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? GetValue(Dictionary<string, string?> values, string key)
    {
        return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }
}
