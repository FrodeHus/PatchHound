using FluentAssertions;
using PatchHound.Core.Entities;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Tests.Infrastructure;

public class IngestionScheduleEvaluatorTests
{
    [Fact]
    public void IsDue_ReturnsTrue_WhenEnabledConfiguredSourceHasPastDueSchedule()
    {
        var source = TenantSourceConfiguration.Create(
            Guid.NewGuid(),
            TenantSourceCatalog.DefenderSourceKey,
            "Microsoft Defender",
            true,
            "0 * * * *",
            "tenant",
            "client",
            "tenants/1/sources/microsoft-defender",
            TenantSourceCatalog.DefaultDefenderApiBaseUrl,
            TenantSourceCatalog.DefaultDefenderTokenScope
        );
        source.UpdateRuntime(
            null,
            new DateTimeOffset(2026, 3, 7, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 7, 10, 5, 0, TimeSpan.Zero),
            null,
            string.Empty,
            string.Empty
        );

        var due = IngestionScheduleEvaluator.IsDue(
            source,
            new DateTimeOffset(2026, 3, 7, 11, 15, 0, TimeSpan.Zero)
        );

        due.Should().BeTrue();
    }

    [Fact]
    public void IsDue_ReturnsFalse_WhenSourceIsDisabledOrMisconfigured()
    {
        var source = TenantSourceConfiguration.Create(
            Guid.NewGuid(),
            TenantSourceCatalog.DefenderSourceKey,
            "Microsoft Defender",
            false,
            "0 * * * *"
        );

        var due = IngestionScheduleEvaluator.IsDue(
            source,
            new DateTimeOffset(2026, 3, 7, 11, 15, 0, TimeSpan.Zero)
        );

        due.Should().BeFalse();
    }
}
