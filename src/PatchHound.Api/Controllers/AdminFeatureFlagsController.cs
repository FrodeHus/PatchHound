using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.FeatureManagement;
using PatchHound.Api.Auth;
using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/admin/feature-flags")]
[Authorize(Policy = Policies.ManageGlobalSettings)]
public class AdminFeatureFlagsController : ControllerBase
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly IFeatureManager _featureManager;

    public AdminFeatureFlagsController(
        PatchHoundDbContext dbContext,
        IFeatureManager featureManager
    )
    {
        _dbContext = dbContext;
        _featureManager = featureManager;
    }

    /// <summary>GET /api/admin/feature-flags — all flags with global state + metadata.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminFeatureFlagDto>>> GetFlags(CancellationToken ct)
    {
        var result = new List<AdminFeatureFlagDto>();

        foreach (var (key, meta) in FeatureFlags.Metadata)
        {
            var isEnabled = await _featureManager.IsEnabledAsync(key);
            result.Add(new AdminFeatureFlagDto(key, meta.DisplayName, meta.Stage.ToString(), isEnabled));
        }

        return Ok(result);
    }

    /// <summary>GET /api/admin/feature-flags/overrides — all overrides, optional ?tenantId= and ?userId= filters.</summary>
    [HttpGet("overrides")]
    public async Task<ActionResult<IReadOnlyList<FeatureFlagOverrideDto>>> GetOverrides(
        [FromQuery] Guid? tenantId,
        [FromQuery] Guid? userId,
        CancellationToken ct
    )
    {
        var query = _dbContext.FeatureFlagOverrides
            .IgnoreQueryFilters()
            .AsNoTracking();

        if (tenantId.HasValue)
            query = query.Where(o => o.TenantId == tenantId.Value);

        if (userId.HasValue)
            query = query.Where(o => o.UserId == userId.Value);

        var overrides = await query
            .OrderBy(o => o.FlagName)
            .ThenBy(o => o.CreatedAt)
            .Select(o => new FeatureFlagOverrideDto(
                o.Id,
                o.FlagName,
                o.TenantId,
                o.UserId,
                o.IsEnabled,
                o.CreatedAt,
                o.ExpiresAt
            ))
            .ToListAsync(ct);

        return Ok(overrides);
    }

    /// <summary>POST /api/admin/feature-flags/overrides — create or upsert an override.</summary>
    [HttpPost("overrides")]
    public async Task<ActionResult<FeatureFlagOverrideDto>> CreateOrUpdateOverride(
        [FromBody] UpsertFeatureFlagOverrideRequest request,
        CancellationToken ct
    )
    {
        if (!FeatureFlags.Metadata.ContainsKey(request.FlagName))
            return BadRequest(new ProblemDetails { Title = $"Unknown feature flag: {request.FlagName}" });

        if (request.TenantId.HasValue == request.UserId.HasValue)
            return BadRequest(new ProblemDetails { Title = "Exactly one of TenantId or UserId must be provided." });

        var now = DateTimeOffset.UtcNow;

        // Check for existing override to upsert
        FeatureFlagOverride? existing = null;
        if (request.TenantId.HasValue)
        {
            existing = await _dbContext.FeatureFlagOverrides
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(o => o.FlagName == request.FlagName && o.TenantId == request.TenantId.Value, ct);
        }
        else if (request.UserId.HasValue)
        {
            existing = await _dbContext.FeatureFlagOverrides
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(o => o.FlagName == request.FlagName && o.UserId == request.UserId.Value, ct);
        }

        if (existing is not null)
        {
            existing.Update(request.IsEnabled, request.ExpiresAt);
        }
        else
        {
            existing = request.TenantId.HasValue
                ? FeatureFlagOverride.CreateTenantOverride(request.FlagName, request.TenantId.Value, request.IsEnabled, request.ExpiresAt)
                : FeatureFlagOverride.CreateUserOverride(request.FlagName, request.UserId!.Value, request.IsEnabled, request.ExpiresAt);

            await _dbContext.FeatureFlagOverrides.AddAsync(existing, ct);
        }

        await _dbContext.SaveChangesAsync(ct);

        return Ok(new FeatureFlagOverrideDto(
            existing.Id,
            existing.FlagName,
            existing.TenantId,
            existing.UserId,
            existing.IsEnabled,
            existing.CreatedAt,
            existing.ExpiresAt
        ));
    }

    /// <summary>DELETE /api/admin/feature-flags/overrides/{id} — delete an override.</summary>
    [HttpDelete("overrides/{id:guid}")]
    public async Task<IActionResult> DeleteOverride(Guid id, CancellationToken ct)
    {
        var existing = await _dbContext.FeatureFlagOverrides
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == id, ct);

        if (existing is null)
            return NotFound(new ProblemDetails { Title = "Override not found." });

        _dbContext.FeatureFlagOverrides.Remove(existing);
        await _dbContext.SaveChangesAsync(ct);
        return NoContent();
    }
}

public record AdminFeatureFlagDto(string FlagName, string DisplayName, string Stage, bool IsEnabled);

public record FeatureFlagOverrideDto(
    Guid Id,
    string FlagName,
    Guid? TenantId,
    Guid? UserId,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt
);

public record UpsertFeatureFlagOverrideRequest(
    string FlagName,
    Guid? TenantId,
    Guid? UserId,
    bool IsEnabled,
    DateTimeOffset? ExpiresAt
);
