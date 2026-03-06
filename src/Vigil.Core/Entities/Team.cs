namespace Vigil.Core.Entities;

public class Team
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;

    private readonly List<TeamMember> _members = [];
    public IReadOnlyCollection<TeamMember> Members => _members.AsReadOnly();

    private Team() { }

    public static Team Create(Guid tenantId, string name)
    {
        return new Team
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name
        };
    }

    public void AddMember(User user)
    {
        if (_members.Any(m => m.UserId == user.Id))
            return;

        _members.Add(TeamMember.Create(Id, user.Id));
    }

    public void RemoveMember(Guid userId)
    {
        var member = _members.FirstOrDefault(m => m.UserId == userId);
        if (member is not null)
            _members.Remove(member);
    }
}
