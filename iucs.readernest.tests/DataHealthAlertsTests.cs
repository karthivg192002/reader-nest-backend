using iucs.readernest.application.Services;
using Xunit;

namespace iucs.readernest.tests;

public class DataHealthAlertsTests
{
    private static readonly DateTime Now = new(2026, 10, 8, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Healthy_data_raises_nothing()
    {
        Assert.Empty(DataHealthAlerts.Build(0, null, DataHealthAlerts.NoShowThreshold - 1, Now));
    }

    [Fact]
    public void Stuck_classes_raise_a_warning_dated_from_the_oldest_one()
    {
        var oldestEnd = new DateTime(2026, 9, 16, 11, 0, 0, DateTimeKind.Utc);

        var alert = Assert.Single(DataHealthAlerts.Build(3, oldestEnd, 0, Now));

        Assert.Equal("ClassesStuckInProgress", alert.Name);
        Assert.Equal("warning", alert.Severity);
        Assert.StartsWith("3 classes", alert.Summary);
        Assert.Equal(oldestEnd.AddHours(1), alert.ActiveSince);
    }

    [Fact]
    public void A_burst_of_no_shows_is_critical()
    {
        // 7 Oct 2026: a second backend on a stale DB marked 107 real classes as teacher no-shows.
        var alert = Assert.Single(DataHealthAlerts.Build(0, null, 107, Now));

        Assert.Equal("UnusualNoShowCount", alert.Name);
        Assert.Equal("critical", alert.Severity);
        Assert.StartsWith("107 classes", alert.Summary);
    }
}
