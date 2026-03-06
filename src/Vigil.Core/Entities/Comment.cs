namespace Vigil.Core.Entities;

public class Comment
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string EntityType { get; private set; } = null!;
    public Guid EntityId { get; private set; }
    public Guid AuthorId { get; private set; }
    public string Content { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    private Comment() { }

    public static Comment Create(Guid tenantId, string entityType, Guid entityId, Guid authorId, string content)
    {
        return new Comment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EntityType = entityType,
            EntityId = entityId,
            AuthorId = authorId,
            Content = content,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void UpdateContent(string content)
    {
        Content = content;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
