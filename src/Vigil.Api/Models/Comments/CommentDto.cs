namespace Vigil.Api.Models.Comments;

public record CommentDto(
    Guid Id,
    string EntityType,
    Guid EntityId,
    Guid AuthorId,
    string Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt
);

public record CreateCommentRequest(string Content);
