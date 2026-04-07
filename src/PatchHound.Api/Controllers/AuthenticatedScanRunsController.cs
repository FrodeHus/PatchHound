using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/authenticated-scan-runs")]
[Authorize(Policy = Policies.ManageAuthenticatedScans)]
public class AuthenticatedScanRunsController(
    PatchHoundDbContext db,
    ITenantContext tenantContext) : ControllerBase
{
    public record ScanRunListDto(
        Guid Id, Guid ScanProfileId, string ProfileName,
        string TriggerKind, Guid? TriggeredByUserId,
        DateTimeOffset StartedAt, DateTimeOffset? CompletedAt,
        string Status, int TotalDevices,
        int SucceededCount, int FailedCount, int EntriesIngested);

    public record ScanRunDetailDto(
        Guid Id, Guid ScanProfileId, string ProfileName,
        string TriggerKind, Guid? TriggeredByUserId,
        DateTimeOffset StartedAt, DateTimeOffset? CompletedAt,
        string Status, int TotalDevices,
        int SucceededCount, int FailedCount, int EntriesIngested,
        List<ScanJobSummaryDto> Jobs);

    public record ScanJobSummaryDto(
        Guid Id, Guid AssetId, string AssetName,
        string Status, int AttemptCount,
        DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt,
        string ErrorMessage, int EntriesIngested,
        List<ValidationIssueDto> ValidationIssues);

    public record ValidationIssueDto(string FieldPath, string Message, int EntryIndex);

    [HttpGet]
    public async Task<ActionResult<PagedResponse<ScanRunListDto>>> List(
        [FromQuery] Guid tenantId,
        [FromQuery] Guid? profileId,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct)
    {
        if (!tenantContext.HasAccessToTenant(tenantId)) return Forbid();

        var query = db.AuthenticatedScanRuns.AsNoTracking()
            .Where(r => r.TenantId == tenantId);

        if (profileId.HasValue)
            query = query.Where(r => r.ScanProfileId == profileId.Value);

        var total = await query.CountAsync(ct);

        var runs = await query
            .OrderByDescending(r => r.StartedAt)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .ToListAsync(ct);

        var profileIds = runs.Select(r => r.ScanProfileId).Distinct().ToList();
        var profileNames = await db.ScanProfiles.AsNoTracking()
            .Where(p => profileIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        var items = runs.Select(r => new ScanRunListDto(
            r.Id, r.ScanProfileId,
            profileNames.GetValueOrDefault(r.ScanProfileId, "\u2014"),
            r.TriggerKind, r.TriggeredByUserId,
            r.StartedAt, r.CompletedAt, r.Status,
            r.TotalDevices, r.SucceededCount, r.FailedCount,
            r.EntriesIngested)).ToList();

        return new PagedResponse<ScanRunListDto>(items, total, pagination.Page, pagination.BoundedPageSize);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ScanRunDetailDto>> GetDetail(Guid id, CancellationToken ct)
    {
        var run = await db.AuthenticatedScanRuns.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (run is null) return NotFound();
        if (!tenantContext.HasAccessToTenant(run.TenantId)) return Forbid();

        var profileName = await db.ScanProfiles.AsNoTracking()
            .Where(p => p.Id == run.ScanProfileId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync(ct) ?? "\u2014";

        var jobs = await db.ScanJobs.AsNoTracking()
            .Where(j => j.RunId == id)
            .OrderBy(j => j.StartedAt)
            .ToListAsync(ct);

        var assetIds = jobs.Select(j => j.AssetId).Distinct().ToList();
        var assetNames = await db.Assets.AsNoTracking()
            .Where(a => assetIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, ct);

        var jobIds = jobs.Select(j => j.Id).ToList();
        var issues = await db.ScanJobValidationIssues.AsNoTracking()
            .Where(v => jobIds.Contains(v.ScanJobId))
            .ToListAsync(ct);
        var issuesByJob = issues
            .GroupBy(v => v.ScanJobId)
            .ToDictionary(g => g.Key, g => g.Select(v =>
                new ValidationIssueDto(v.FieldPath, v.Message, v.EntryIndex)).ToList());

        var jobDtos = jobs.Select(j => new ScanJobSummaryDto(
            j.Id, j.AssetId,
            assetNames.GetValueOrDefault(j.AssetId, "\u2014"),
            j.Status, j.AttemptCount,
            j.StartedAt, j.CompletedAt,
            j.ErrorMessage, j.EntriesIngested,
            issuesByJob.GetValueOrDefault(j.Id, []))).ToList();

        return new ScanRunDetailDto(
            run.Id, run.ScanProfileId, profileName,
            run.TriggerKind, run.TriggeredByUserId,
            run.StartedAt, run.CompletedAt, run.Status,
            run.TotalDevices, run.SucceededCount, run.FailedCount,
            run.EntriesIngested, jobDtos);
    }
}
