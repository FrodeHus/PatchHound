using PatchHound.Core.Entities;

namespace PatchHound.Core.Models;

public record TenantAiProfileResolved(
    TenantAiProfile Profile,
    string ApiKey
);
