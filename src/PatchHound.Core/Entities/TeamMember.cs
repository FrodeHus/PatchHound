namespace PatchHound.Core.Entities;

public class TeamMember
{
    public Guid Id { get; private set; }
    public Guid TeamId { get; private set; }
    public Guid UserId { get; private set; }

    public Team Team { get; private set; } = null!;
    public User User { get; private set; } = null!;

    private TeamMember() { }

    public static TeamMember Create(Guid teamId, Guid userId)
    {
        return new TeamMember
        {
            Id = Guid.NewGuid(),
            TeamId = teamId,
            UserId = userId,
        };
    }
}
