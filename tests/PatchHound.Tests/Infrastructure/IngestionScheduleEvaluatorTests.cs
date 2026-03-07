using FluentAssertions;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Tests.Infrastructure;

public class IngestionScheduleEvaluatorTests
{
    [Fact]
    public void IsDue_ReturnsTrue_WhenEnabledConfiguredSourceHasPastDueSchedule()
    {
        var source = new PersistedIngestionSource
        {
            Key = TenantSourceSettings.DefenderSourceKey,
            Enabled = true,
            SyncSchedule = "0 * * * *",
            Credentials = new PersistedSourceCredentials
            {
                TenantId = "tenant",
                ClientId = "client",
                SecretRef = "tenants/1/sources/microsoft-defender",
            },
            Runtime = new PersistedIngestionRuntimeState
            {
                LastStartedAt = new DateTimeOffset(2026, 3, 7, 10, 0, 0, TimeSpan.Zero),
                LastCompletedAt = new DateTimeOffset(2026, 3, 7, 10, 5, 0, TimeSpan.Zero),
            },
        };

        var due = IngestionScheduleEvaluator.IsDue(
            source,
            new DateTimeOffset(2026, 3, 7, 11, 15, 0, TimeSpan.Zero)
        );

        due.Should().BeTrue();
    }

    [Fact]
    public void IsDue_ReturnsFalse_WhenSourceIsDisabledOrMisconfigured()
    {
        var source = new PersistedIngestionSource
        {
            Key = TenantSourceSettings.DefenderSourceKey,
            Enabled = false,
            SyncSchedule = "0 * * * *",
            Credentials = new PersistedSourceCredentials(),
        };

        var due = IngestionScheduleEvaluator.IsDue(
            source,
            new DateTimeOffset(2026, 3, 7, 11, 15, 0, TimeSpan.Zero)
        );

        due.Should().BeFalse();
    }
}
