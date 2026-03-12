using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Services;

public static class IngestionRunStatePolicy
{
    public static bool IsActive(string status)
    {
        return status is IngestionRunStatuses.Staging
            or IngestionRunStatuses.MergePending
            or IngestionRunStatuses.Merging;
    }
}
