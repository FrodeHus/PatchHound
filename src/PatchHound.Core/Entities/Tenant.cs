namespace PatchHound.Core.Entities;

public class Tenant
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string EntraTenantId { get; private set; } = null!;
    public bool IsPrimary { get; private set; }
    public bool IsPendingDeletion { get; private set; }

    private Tenant() { }

    public static Tenant Create(string name, string entraTenantId, bool isPrimary = false)
    {
        return new Tenant
        {
            Id = Guid.NewGuid(),
            Name = name,
            EntraTenantId = entraTenantId,
            IsPrimary = isPrimary,
        };
    }

    public void UpdateName(string name)
    {
        Name = name;
    }

    public void MarkPendingDeletion()
    {
        IsPendingDeletion = true;
    }
}
