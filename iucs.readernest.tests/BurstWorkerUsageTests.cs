using iucs.readernest.application.Services;
using Xunit;

namespace iucs.readernest.tests
{
    /// <summary>
    /// The burst worker's cost panel is money-related and its Start button builds a shell command,
    /// so the arithmetic, the event pairing and the injection guard are pinned down here.
    /// </summary>
    public class BurstWorkerUsageTests
    {
        private const double Rate = 0.1763;
        // 2026-09-19 12:00 UTC = 17:30 IST on 19 Sep.
        private static readonly DateTime Now = new(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void Pairs_created_and_deleted_and_prices_by_minute_and_by_started_hour()
        {
            var log =
                "{\"ts\":\"2026-09-19T10:00:00Z\",\"event\":\"created\",\"serverId\":1}\n" +
                "{\"ts\":\"2026-09-19T11:30:00Z\",\"event\":\"deleted\",\"serverId\":1}\n";

            var usage = BurstWorkerUsageParser.Parse(log, Now, Rate);

            var ep = Assert.Single(usage.RecentEpisodes);
            Assert.Equal(1.5, ep.DurationHours, 3);
            Assert.Equal(1.5 * Rate, ep.EstimatedCostUsd, 4);
            Assert.Equal(2, ep.BilledHours);                       // 1.5h rounds UP to 2 started hours
            Assert.Equal(2 * Rate, ep.MaxBilledCostUsd, 4);
            Assert.False(usage.CurrentlyActive);
        }

        [Fact]
        public void A_very_short_episode_is_billed_as_at_least_one_hour_in_the_upper_bound()
        {
            var log =
                "{\"ts\":\"2026-09-19T10:00:00Z\",\"event\":\"created\",\"serverId\":7}\n" +
                "{\"ts\":\"2026-09-19T10:00:14Z\",\"event\":\"deleted\",\"serverId\":7}\n";   // the 14-second incident

            var ep = Assert.Single(BurstWorkerUsageParser.Parse(log, Now, Rate).RecentEpisodes);

            Assert.True(ep.EstimatedCostUsd < 0.001);
            Assert.Equal(1, ep.BilledHours);
            Assert.Equal(Rate, ep.MaxBilledCostUsd, 4);
        }

        [Fact]
        public void An_open_episode_is_running_now_and_costs_time_so_far()
        {
            var log = "{\"ts\":\"2026-09-19T11:00:00Z\",\"event\":\"created\",\"serverId\":2,\"trigger\":\"manual\",\"by\":\"ops@example.com\"}\n";

            var usage = BurstWorkerUsageParser.Parse(log, Now, Rate);
            var ep = Assert.Single(usage.RecentEpisodes);

            Assert.True(usage.CurrentlyActive);
            Assert.Null(ep.DeletedAtUtc);
            Assert.Equal(1.0, ep.DurationHours, 3);
            Assert.Equal("manual", ep.Trigger);
            Assert.Equal("ops@example.com", ep.RequestedBy);
        }

        [Fact]
        public void Old_log_lines_without_trigger_default_to_auto()
        {
            var log =
                "{\"ts\":\"2026-09-18T00:56:59Z\",\"event\":\"created\",\"serverId\":3}\n" +
                "{\"ts\":\"2026-09-18T01:06:38Z\",\"event\":\"deleted\",\"serverId\":3}\n";

            Assert.Equal("auto", Assert.Single(BurstWorkerUsageParser.Parse(log, Now, Rate).RecentEpisodes).Trigger);
        }

        [Fact]
        public void Rescued_recordings_are_counted_against_the_episode_they_happened_in()
        {
            var log =
                "{\"ts\":\"2026-09-19T08:00:00Z\",\"event\":\"created\",\"serverId\":10}\n" +
                "{\"ts\":\"2026-09-19T08:20:00Z\",\"event\":\"recording\",\"dir\":\"aaa\"}\n" +
                "{\"ts\":\"2026-09-19T08:40:00Z\",\"event\":\"recording\",\"dir\":\"bbb\"}\n" +
                "{\"ts\":\"2026-09-19T09:00:00Z\",\"event\":\"deleted\",\"serverId\":10}\n" +
                "{\"ts\":\"2026-09-19T10:00:00Z\",\"event\":\"created\",\"serverId\":11}\n" +
                "{\"ts\":\"2026-09-19T10:30:00Z\",\"event\":\"recording\",\"dir\":\"ccc\"}\n" +
                "{\"ts\":\"2026-09-19T11:00:00Z\",\"event\":\"deleted\",\"serverId\":11}\n";

            var usage = BurstWorkerUsageParser.Parse(log, Now, Rate);

            Assert.Equal(2, usage.RecentEpisodes.Single(e => e.ServerId == 10).RecordingsHandled);
            Assert.Equal(1, usage.RecentEpisodes.Single(e => e.ServerId == 11).RecordingsHandled);
            Assert.Equal(3, usage.RecordingsHandledToday);
        }

        [Fact]
        public void Today_and_month_use_the_IST_boundary_not_UTC()
        {
            // 18 Sep 19:00 UTC = 19 Sep 00:30 IST, i.e. already "today" (19 Sep) for the admin.
            var log =
                "{\"ts\":\"2026-09-18T19:00:00Z\",\"event\":\"created\",\"serverId\":20}\n" +
                "{\"ts\":\"2026-09-18T20:00:00Z\",\"event\":\"deleted\",\"serverId\":20}\n" +
                // 18 Sep 10:00 UTC = 18 Sep 15:30 IST -> yesterday, same month
                "{\"ts\":\"2026-09-18T10:00:00Z\",\"event\":\"created\",\"serverId\":21}\n" +
                "{\"ts\":\"2026-09-18T11:00:00Z\",\"event\":\"deleted\",\"serverId\":21}\n" +
                // 31 Aug 20:00 UTC = 1 Sep 01:30 IST -> September already
                "{\"ts\":\"2026-08-31T20:00:00Z\",\"event\":\"created\",\"serverId\":22}\n" +
                "{\"ts\":\"2026-08-31T21:00:00Z\",\"event\":\"deleted\",\"serverId\":22}\n" +
                // 30 Aug -> August
                "{\"ts\":\"2026-08-30T10:00:00Z\",\"event\":\"created\",\"serverId\":23}\n" +
                "{\"ts\":\"2026-08-30T11:00:00Z\",\"event\":\"deleted\",\"serverId\":23}\n";

            var usage = BurstWorkerUsageParser.Parse(log, Now, Rate);

            Assert.Equal(1, usage.EpisodesToday);
            Assert.Equal(3, usage.EpisodesThisMonth);
            Assert.Equal(4, usage.EpisodesAllTime);
        }

        [Fact]
        public void Malformed_lines_and_a_deleted_without_a_created_are_ignored_not_fatal()
        {
            var log =
                "not json at all\n" +
                "{\"ts\":\"2026-09-19T10:00:00Z\",\"event\":\"deleted\",\"serverId\":99}\n" +
                "{\"event\":\"created\"}\n" +
                "{\"ts\":\"2026-09-19T10:00:00Z\",\"event\":\"created\",\"serverId\":30}\n" +
                "{\"ts\":\"2026-09-19T10:30:00Z\",\"event\":\"deleted\",\"serverId\":30}\n";

            var usage = BurstWorkerUsageParser.Parse(log, Now, Rate);

            Assert.Equal(1, usage.EpisodesAllTime);
        }

        [Fact]
        public void Empty_log_gives_zeroes()
        {
            var usage = BurstWorkerUsageParser.Parse(string.Empty, Now, Rate);

            Assert.Equal(0, usage.EpisodesAllTime);
            Assert.Equal(0.0, usage.EstimatedCostAllTimeUsd);
            Assert.False(usage.CurrentlyActive);
        }

        [Fact]
        public void Status_reports_existence_and_an_active_hold()
        {
            var holdEpoch = new DateTimeOffset(Now.AddMinutes(90)).ToUnixTimeSeconds();
            var json = $"{{\"ok\": true, \"exists\": true, \"serverId\": 555, \"createdAtUtc\": \"2026-09-19T11:00:00Z\", \"holdUntilEpoch\": {holdEpoch}}}";

            var status = BurstWorkerUsageParser.ParseStatus(json, Now);

            Assert.True(status.Known);
            Assert.True(status.Exists);
            Assert.Equal(555, status.ServerId);
            Assert.Equal(Now.AddMinutes(90), status.HoldUntilUtc);
        }

        [Fact]
        public void An_expired_hold_is_not_reported()
        {
            var expired = new DateTimeOffset(Now.AddMinutes(-5)).ToUnixTimeSeconds();
            var status = BurstWorkerUsageParser.ParseStatus($"{{\"ok\": true, \"exists\": false, \"holdUntilEpoch\": {expired}}}", Now);

            Assert.True(status.Known);
            Assert.False(status.Exists);
            Assert.Null(status.HoldUntilUtc);
        }

        [Theory]
        [InlineData("{\"ok\": false, \"exists\": false}")]
        [InlineData("garbage")]
        [InlineData("")]
        public void When_the_cloud_API_could_not_be_read_status_is_unknown_not_standby(string json)
        {
            Assert.False(BurstWorkerUsageParser.ParseStatus(json, Now).Known);
        }

        [Fact]
        public void Activity_keeps_only_timestamped_lines_and_the_most_recent_ones_in_order()
        {
            var text =
                "Warning: Permanently added '10.10.10.1' (ED25519) to the list of known hosts.\n" +
                "2026-09-19T01:00:00Z scale-up: created x\n" +
                "sending incremental file list\n" +
                "2026-09-19T01:05:00Z scale-down skipped: still busy\n" +
                "2026-09-19T01:10:00Z scale-down: deleted x\n";

            var lines = BurstWorkerUsageParser.ParseActivity(text, 2);

            Assert.Equal(2, lines.Count);
            Assert.StartsWith("2026-09-19T01:05:00Z", lines[0]);
            Assert.StartsWith("2026-09-19T01:10:00Z", lines[1]);
        }

        [Theory]
        [InlineData("ops@example.com", "ops@example.com")]
        [InlineData("Priya Sharma", "Priya Sharma")]
        [InlineData("x'; rm -rf / #", "x rm -rf")]           // quote, semicolon and hash are stripped
        [InlineData("$(reboot)", "reboot")]
        [InlineData("`id`", "id")]
        [InlineData("a\nb", "ab")]
        [InlineData("", "admin")]
        [InlineData(null, "admin")]
        [InlineData("';'\"", "admin")]
        public void Requester_name_can_never_break_out_of_the_shell_command(string? input, string expected)
        {
            var cleaned = BurstWorkerControlService.SanitizeName(input);

            Assert.Equal(expected, cleaned);
            Assert.DoesNotContain("'", cleaned);
            Assert.DoesNotContain(";", cleaned);
            Assert.DoesNotContain("$", cleaned);
            Assert.DoesNotContain("`", cleaned);
        }

        [Fact]
        public void Requester_name_is_length_limited()
        {
            Assert.Equal(60, BurstWorkerControlService.SanitizeName(new string('a', 500)).Length);
        }
    }
}
