namespace PatchHound.Core.Entities;

public class CloudApplication
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SourceSystemId { get; private set; }
    public string ExternalId { get; private set; } = string.Empty;
    public string? AppId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsFallbackPublicClient { get; private set; }
    public IReadOnlyList<string> RedirectUris { get; private set; } = [];
    public Guid? OwnerTeamId { get; private set; }
    public Guid? OwnerTeamRuleId { get; private set; }
    public bool ActiveInTenant { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public ICollection<CloudApplicationCredentialMetadata> Credentials { get; private set; } =
        new List<CloudApplicationCredentialMetadata>();

    private CloudApplication() { }

    public static CloudApplication Create(
        Guid tenantId,
        Guid sourceSystemId,
        string externalId,
        string? appId,
        string name,
        string? description,
        bool isFallbackPublicClient,
        IReadOnlyList<string> redirectUris
    )
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (sourceSystemId == Guid.Empty)
            throw new ArgumentException("SourceSystemId is required.", nameof(sourceSystemId));
        if (string.IsNullOrWhiteSpace(externalId))
            throw new ArgumentException("ExternalId is required.", nameof(externalId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        var now = DateTimeOffset.UtcNow;
        return new CloudApplication
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SourceSystemId = sourceSystemId,
            ExternalId = externalId.Trim(),
            AppId = appId?.Trim(),
            Name = name.Trim(),
            Description = description?.Trim(),
            IsFallbackPublicClient = isFallbackPublicClient,
            RedirectUris = redirectUris,
            ActiveInTenant = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(string name, string? description, string? appId, bool isFallbackPublicClient, IReadOnlyList<string> redirectUris)
    {
        Name = name.Trim();
        Description = description?.Trim();
        AppId = appId?.Trim();
        IsFallbackPublicClient = isFallbackPublicClient;
        RedirectUris = redirectUris;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AssignOwnerTeam(Guid? teamId)
    {
        OwnerTeamId = teamId;
        OwnerTeamRuleId = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AssignOwnerTeamFromRule(Guid? teamId, Guid ruleId)
    {
        OwnerTeamId = teamId;
        OwnerTeamRuleId = teamId.HasValue ? ruleId : null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ClearRuleAssignedOwnerTeam(Guid ruleId)
    {
        if (OwnerTeamRuleId != ruleId)
        {
            return;
        }

        OwnerTeamId = null;
        OwnerTeamRuleId = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetActiveInTenant(bool active)
    {
        ActiveInTenant = active;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
