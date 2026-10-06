using iucs.readernest.application.Services;
using Xunit;

namespace iucs.readernest.tests;

public class ScheduledTaskStatusTests
{
    private static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Recent_successful_run_is_ok()
    {
        Assert.Equal(ScheduledTaskStatus.Ok, ScheduledTaskStatus.Classify(Now.AddSeconds(-50), 0, 60, Now));
    }

    [Fact]
    public void Recent_run_with_non_zero_exit_is_failed()
    {
        Assert.Equal(ScheduledTaskStatus.Failed, ScheduledTaskStatus.Classify(Now.AddMinutes(-2), 1, 300, Now));
    }

    [Fact]
    public void Daily_task_that_ran_20_hours_ago_is_not_late()
    {
        Assert.Equal(ScheduledTaskStatus.Ok, ScheduledTaskStatus.Classify(Now.AddHours(-20), 0, 86400, Now));
    }

    [Theory]
    [InlineData(60, 7 * 60 + 30)]     // every-minute task silent for 7.5 min (> 2x60s + 5 min = 7 min)
    [InlineData(3600, 2 * 3600 + 400)] // hourly task silent for just over 2h05m
    public void Task_silent_past_twice_its_interval_plus_five_minutes_is_late(double intervalSeconds, int silentSeconds)
    {
        Assert.Equal(ScheduledTaskStatus.Late, ScheduledTaskStatus.Classify(Now.AddSeconds(-silentSeconds), 0, intervalSeconds, Now));
    }

    [Fact]
    public void Late_wins_over_failed_because_the_stale_exit_code_is_no_longer_news()
    {
        Assert.Equal(ScheduledTaskStatus.Late, ScheduledTaskStatus.Classify(Now.AddHours(-1), 2, 60, Now));
    }
}
