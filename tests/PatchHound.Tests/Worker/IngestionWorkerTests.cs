using FluentAssertions;
using PatchHound.Worker;

namespace PatchHound.Tests.Worker;

public class IngestionWorkerTests
{
    [Fact]
    public void HasConfiguredCredentials_ReturnsTrue_WhenStoredCredentialIsSelected()
    {
        var source = new IngestionWorker.ScheduledSource(
            Id: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            TenantName: "Tenant",
            SourceKey: "microsoft-defender",
            Enabled: true,
            CredentialTenantId: string.Empty,
            ClientId: string.Empty,
            SecretRef: string.Empty,
            ApiBaseUrl: "https://api.securitycenter.microsoft.com",
            TokenScope: "https://api.securitycenter.microsoft.com/.default",
            SyncSchedule: "0 * * * *",
            StoredCredentialId: Guid.NewGuid(),
            ManualRequestedAt: DateTimeOffset.UtcNow,
            LastStartedAt: null
        );

        IngestionWorker.HasConfiguredCredentials(source).Should().BeTrue();
    }
}
