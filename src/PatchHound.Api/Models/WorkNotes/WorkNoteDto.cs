namespace PatchHound.Api.Models.WorkNotes;

public record WorkNoteDto(
    Guid Id,
    string EntityType,
    Guid EntityId,
    Guid AuthorId,
    string AuthorDisplayName,
    string Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    bool CanEdit,
    bool CanDelete
);

public record CreateWorkNoteRequest(string Content);

public record UpdateWorkNoteRequest(string Content);
