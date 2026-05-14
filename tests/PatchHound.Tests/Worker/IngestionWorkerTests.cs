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
            LastStartedAt: null,
            LastCompletedAt: null
        );

        IngestionWorker.HasConfiguredCredentials(source).Should().BeTrue();
    }

    [Fact]
    public void BuildAssessmentRequest_UsesProviderNativeWebResearch()
    {
        var request = VulnerabilityAssessmentWorker.BuildAssessmentRequest("CVE-2026-4242");

        request.UseProviderNativeWebResearch.Should().BeTrue();
        request.MaxResearchSources.Should().Be(10);
        request.UserPrompt.Should().Contain("CVE-2026-4242");
    }
}
