using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Vigil.Api.Auth;
using Vigil.Api.Models.Comments;
using Vigil.Core.Entities;
using Vigil.Core.Interfaces;
using Vigil.Infrastructure.Data;

namespace Vigil.Api.Controllers;

[ApiController]
[Route("api/{entityType}/{entityId:guid}/comments")]
[Authorize]
public class CommentsController : ControllerBase
{
    private static readonly Dictionary<string, string> EntityTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["vulnerabilities"] = "Vulnerability",
        ["tasks"] = "RemediationTask",
        ["campaigns"] = "Campaign",
    };

    private readonly VigilDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public CommentsController(VigilDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<List<CommentDto>>> List(
        string entityType,
        Guid entityId,
        CancellationToken ct
    )
    {
        if (!EntityTypeMap.TryGetValue(entityType, out var internalEntityType))
            return BadRequest(new ProblemDetails { Title = "Invalid entity type" });

        var comments = await _dbContext
            .Comments.AsNoTracking()
            .Where(c => c.EntityType == internalEntityType && c.EntityId == entityId)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new CommentDto(
                c.Id,
                c.EntityType,
                c.EntityId,
                c.AuthorId,
                c.Content,
                c.CreatedAt,
                c.UpdatedAt
            ))
            .ToListAsync(ct);

        return Ok(comments);
    }

    [HttpPost]
    [Authorize(Policy = Policies.AddComments)]
    public async Task<ActionResult<CommentDto>> Create(
        string entityType,
        Guid entityId,
        [FromBody] CreateCommentRequest request,
        CancellationToken ct
    )
    {
        if (!EntityTypeMap.TryGetValue(entityType, out var internalEntityType))
            return BadRequest(new ProblemDetails { Title = "Invalid entity type" });

        if (_tenantContext.CurrentTenantId is null)
            return Unauthorized();

        var comment = Comment.Create(
            _tenantContext.CurrentTenantId.Value,
            internalEntityType,
            entityId,
            _tenantContext.CurrentUserId,
            request.Content
        );

        _dbContext.Comments.Add(comment);
        await _dbContext.SaveChangesAsync(ct);

        var dto = new CommentDto(
            comment.Id,
            comment.EntityType,
            comment.EntityId,
            comment.AuthorId,
            comment.Content,
            comment.CreatedAt,
            comment.UpdatedAt
        );

        return Created($"/api/{entityType}/{entityId}/comments/{comment.Id}", dto);
    }
}
