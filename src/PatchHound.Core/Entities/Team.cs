namespace PatchHound.Core.Entities;

public class Team
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public bool IsDefault { get; private set; }
    public bool IsDynamic { get; private set; }

    private readonly List<TeamMember> _members = [];
    public IReadOnlyCollection<TeamMember> Members => _members.AsReadOnly();

    private Team() { }

    public static Team Create(Guid tenantId, string name)
    {
        return new Team
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            IsDefault = false,
            IsDynamic = false,
        };
    }

    public static Team CreateDefault(Guid tenantId, string name)
    {
        return new Team
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            IsDefault = true,
            IsDynamic = false,
        };
    }

    public void Rename(string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        if (newName.Length > 256)
            throw new ArgumentException("Name must be 256 characters or fewer.", nameof(newName));

        Name = newName;
    }

    public void SetDynamic(bool isDynamic)
    {
        IsDynamic = isDynamic;
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
