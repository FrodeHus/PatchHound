namespace PatchHound.Core.Entities;

public class Tenant
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string EntraTenantId { get; private set; } = null!;

    private Tenant() { }

    public static Tenant Create(string name, string entraTenantId)
    {
        return new Tenant
        {
            Id = Guid.NewGuid(),
            Name = name,
            EntraTenantId = entraTenantId,
        };
    }

    public void UpdateName(string name)
    {
        Name = name;
    }
}
