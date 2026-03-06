using System.Text.Json;
using FluentAssertions;
using Vigil.Core.Enums;
using Vigil.Core.Services;

namespace Vigil.Tests.Core;

public class SlaServiceTests
{
    private readonly SlaService _slaService = new();
    private readonly DateTimeOffset _baseDate = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(Severity.Critical, 7)]
    [InlineData(Severity.High, 30)]
    [InlineData(Severity.Medium, 90)]
    [InlineData(Severity.Low, 180)]
    public void CalculateDueDate_DefaultSla_UsesCorrectDays(Severity severity, int expectedDays)
    {
        var dueDate = _slaService.CalculateDueDate(severity, _baseDate);

        dueDate.Should().Be(_baseDate.AddDays(expectedDays));
    }

    [Fact]
    public void CalculateDueDate_NullSettings_UsesDefaults()
    {
        var dueDate = _slaService.CalculateDueDate(Severity.Critical, _baseDate, null);

        dueDate.Should().Be(_baseDate.AddDays(7));
    }

    [Fact]
    public void CalculateDueDate_EmptySettings_UsesDefaults()
    {
        var dueDate = _slaService.CalculateDueDate(Severity.High, _baseDate, "{}");

        dueDate.Should().Be(_baseDate.AddDays(30));
    }

    [Fact]
    public void CalculateDueDate_TenantOverride_UsesOverriddenDays()
    {
        var settings = JsonSerializer.Serialize(new
        {
            SlaDays = new Dictionary<string, int>
            {
                { "Critical", 3 },
                { "High", 14 },
            }
        });

        var criticalDueDate = _slaService.CalculateDueDate(Severity.Critical, _baseDate, settings);
        criticalDueDate.Should().Be(_baseDate.AddDays(3));

        var highDueDate = _slaService.CalculateDueDate(Severity.High, _baseDate, settings);
        highDueDate.Should().Be(_baseDate.AddDays(14));
    }

    [Fact]
    public void CalculateDueDate_PartialOverride_FallsBackToDefaultForMissingSeverities()
    {
        var settings = JsonSerializer.Serialize(new
        {
            SlaDays = new Dictionary<string, int>
            {
                { "Critical", 5 },
            }
        });

        var criticalDueDate = _slaService.CalculateDueDate(Severity.Critical, _baseDate, settings);
        criticalDueDate.Should().Be(_baseDate.AddDays(5));

        // Medium not overridden, should use default of 90
        var mediumDueDate = _slaService.CalculateDueDate(Severity.Medium, _baseDate, settings);
        mediumDueDate.Should().Be(_baseDate.AddDays(90));
    }

    [Fact]
    public void CalculateDueDate_InvalidJson_UsesDefaults()
    {
        var dueDate = _slaService.CalculateDueDate(Severity.Critical, _baseDate, "not-valid-json");

        dueDate.Should().Be(_baseDate.AddDays(7));
    }

    [Fact]
    public void GetSlaStatus_OnTrack_WhenLessThan75PercentElapsed()
    {
        var createdAt = _baseDate;
        var dueDate = _baseDate.AddDays(100);
        var now = _baseDate.AddDays(50); // 50% elapsed

        var status = _slaService.GetSlaStatus(createdAt, dueDate, now);

        status.Should().Be(SlaStatus.OnTrack);
    }

    [Fact]
    public void GetSlaStatus_NearDue_When75PercentElapsed()
    {
        var createdAt = _baseDate;
        var dueDate = _baseDate.AddDays(100);
        var now = _baseDate.AddDays(75); // exactly 75%

        var status = _slaService.GetSlaStatus(createdAt, dueDate, now);

        status.Should().Be(SlaStatus.NearDue);
    }

    [Fact]
    public void GetSlaStatus_NearDue_When90PercentElapsed()
    {
        var createdAt = _baseDate;
        var dueDate = _baseDate.AddDays(100);
        var now = _baseDate.AddDays(90); // 90%

        var status = _slaService.GetSlaStatus(createdAt, dueDate, now);

        status.Should().Be(SlaStatus.NearDue);
    }

    [Fact]
    public void GetSlaStatus_Overdue_When100PercentElapsed()
    {
        var createdAt = _baseDate;
        var dueDate = _baseDate.AddDays(100);
        var now = _baseDate.AddDays(100); // exactly 100%

        var status = _slaService.GetSlaStatus(createdAt, dueDate, now);

        status.Should().Be(SlaStatus.Overdue);
    }

    [Fact]
    public void GetSlaStatus_Overdue_WhenPastDueDate()
    {
        var createdAt = _baseDate;
        var dueDate = _baseDate.AddDays(7);
        var now = _baseDate.AddDays(10); // past due

        var status = _slaService.GetSlaStatus(createdAt, dueDate, now);

        status.Should().Be(SlaStatus.Overdue);
    }

    [Fact]
    public void GetSlaDays_ReturnsCorrectDefaultForEachSeverity()
    {
        _slaService.GetSlaDays(Severity.Critical).Should().Be(7);
        _slaService.GetSlaDays(Severity.High).Should().Be(30);
        _slaService.GetSlaDays(Severity.Medium).Should().Be(90);
        _slaService.GetSlaDays(Severity.Low).Should().Be(180);
    }
}
