using iucs.readernest.application.Services;
using Xunit;

namespace iucs.readernest.tests;

public class RecordingPipelineParserTests
{
    // 2026-09-21 06:00 IST == 00:30 UTC; IST "today" starts 2026-09-20 18:30 UTC.
    private static readonly DateTime Now = new(2026, 9, 21, 0, 30, 0, DateTimeKind.Utc);
    private const string Disk = "322122547200 223338299392"; // 300 GiB total, ~208 GiB free
    private const long Gb = 1024L * 1024 * 1024;

    private static string Line(string at, string room, int dur, int code) =>
        $"{at} room={room} storageUrl=https://x/{room}_{at}.mp4 duration={dur} -> HTTP {code} {{}}";

    [Fact]
    public void Counts_registered_unattached_and_failed_for_today_only()
    {
        var log = string.Join('\n',
            "2026-09-20T03:00:00Z invoked for /x", // ignored line
            Line("2026-09-20T10:00:00Z", "trn-old", 100, 200), // before today's IST start: not counted
            Line("2026-09-20T19:00:00Z", "trn-a", 600, 200),
            Line("2026-09-20T20:00:00Z", "trn-b", 375, 204),
            Line("2026-09-20T21:00:00Z", "trn-c", 90, 500));

        var dto = RecordingPipelineParser.Parse(log, 0, null, Disk, 0, Now);

        Assert.Equal(1, dto.RegisteredToday);
        Assert.Equal(1, dto.UnattachedToday);
        Assert.Equal(1, dto.FailedToday);
        Assert.Equal("trn-b", dto.Unattached.Single().Room);
        Assert.Equal(375, dto.Unattached.Single().DurationSeconds);
    }

    [Fact]
    public void A_failure_that_was_retried_successfully_counts_once_as_registered()
    {
        var failed = "2026-09-20T19:00:00Z room=trn-a storageUrl=https://x/a.mp4 duration=600 -> HTTP 500 ";
        var ok = "2026-09-20T19:01:00Z room=trn-a storageUrl=https://x/a.mp4 duration=600 -> HTTP 200 {}";

        var dto = RecordingPipelineParser.Parse(failed + "\n" + ok, 0, null, Disk, 0, Now);

        Assert.Equal(1, dto.RegisteredToday);
        Assert.Equal(0, dto.FailedToday);
    }

    [Fact]
    public void Unattached_list_is_newest_first()
    {
        var log = Line("2026-09-20T19:00:00Z", "trn-1", 10, 204) + "\n" + Line("2026-09-20T22:00:00Z", "trn-2", 10, 204);

        var dto = RecordingPipelineParser.Parse(log, 0, null, Disk, 0, Now);

        Assert.Equal(new[] { "trn-2", "trn-1" }, dto.Unattached.Select(u => u.Room));
    }

    [Fact]
    public void Disk_headroom_and_days_left_are_computed_from_growth()
    {
        var dto = RecordingPipelineParser.Parse("", 3, 1_790_000_000, Disk, 13 * Gb, Now);

        Assert.Equal(3, dto.PendingRegistration);
        Assert.Equal(300, dto.DiskTotalGb);
        Assert.Equal(208, dto.DiskFreeGb);
        Assert.Equal(69.3, dto.DiskFreePercent);
        Assert.Equal(13, dto.GrowthLast24hGb);
        Assert.Equal(16, dto.DaysOfDiskLeft);
        Assert.NotNull(dto.NewestRecordingUtc);
    }

    [Fact]
    public void No_growth_means_no_days_left_estimate_and_garbage_input_does_not_throw()
    {
        var dto = RecordingPipelineParser.Parse("garbage\n\n", -4, null, "not numbers", 0, Now);

        Assert.Null(dto.DaysOfDiskLeft);
        Assert.Equal(0, dto.PendingRegistration);
        Assert.Equal(0, dto.DiskTotalGb);
        Assert.Null(dto.NewestRecordingUtc);
    }
}
