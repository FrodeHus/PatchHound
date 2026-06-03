using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models.Ai;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models.OperationalContext;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/ai/context")]
[Authorize(Policy = Policies.ViewVulnerabilities)]
public class AiOperationalContextController(
    IAiOperationalContextService contextService,
    ITenantContext tenantContext,
    PatchHoundDbContext dbContext) : ControllerBase
{
    [HttpGet("remediation-cases/{caseId:guid}")]
    public async Task<ActionResult<OperationalContextPreviewDto>> PreviewRemediationCase(
        Guid caseId, CancellationToken ct)
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var options = await ResolveOptionsAsync(tenantId, ct);
        var result = await contextService.BuildForRemediationCaseAsync(tenantId, caseId, options, ct);
        return Ok(ToDto(result, caseId));
    }

    [HttpGet("vulnerabilities/{vulnerabilityId:guid}")]
    public async Task<ActionResult<OperationalContextPreviewDto>> PreviewVulnerability(
        Guid vulnerabilityId, CancellationToken ct)
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var options = await ResolveOptionsAsync(tenantId, ct);
        var result = await contextService.BuildForVulnerabilityAsync(tenantId, vulnerabilityId, options, ct);
        return Ok(ToDto(result, vulnerabilityId));
    }

    private async Task<AiOperationalContextOptions> ResolveOptionsAsync(Guid tenantId, CancellationToken ct)
    {
        var profile = await dbContext.TenantAiProfiles.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.IsDefault)
            .Select(p => new
            {
                p.ProviderType,
                p.MaxOperationalContextTokens,
                p.IncludeDeviceNamesInContext,
                p.IncludeUserNamesInContext,
            })
            .FirstOrDefaultAsync(ct);

        return new AiOperationalContextOptions
        {
            MaxTokens = profile?.MaxOperationalContextTokens ?? 3000,
            ProviderIsExternal = profile?.ProviderType.IsExternal() ?? false,
            IncludeDeviceNames = profile?.IncludeDeviceNamesInContext ?? true,
            IncludeUserNames = profile?.IncludeUserNamesInContext ?? false,
        };
    }

    private static OperationalContextPreviewDto ToDto(AiOperationalContextResult result, Guid subjectId) =>
        new(
            result.Pack.ContextKind,
            subjectId,
            result.TokenEstimate,
            result.Truncated,
            DateTimeOffset.UtcNow,
            result.Pack,
            result.Citations);
}
