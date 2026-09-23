using iucs.readernest.application.Services;
using Xunit;

namespace iucs.readernest.tests;

public class CallQualityIncidentParserTests
{
    // 2026-09-23 05:35 IST == 00:05 UTC
    private static readonly DateTime Now = new(2026, 9, 23, 0, 5, 0, DateTimeKind.Utc);

    private static string Line(string localTime, string room, string ep, string label, string kbps) =>
        $"JVB {localTime}.062 WARNING: [35] [confId=b310b93a4818ae1f conf_name={room}@muc.meet.jitsi " +
        $"meeting_id=8453886c epId={ep} stats_id={label}] " +
        $"SendSideBandwidthEstimation.maybeLogLowBitrateWarning-SimD6oM#544: " +
        $"Estimated available bandwidth {kbps} kbps is below configured min bitrate 30 kbps.";

    [Fact]
    public void Groups_repeated_warnings_for_the_same_participant_into_one_incident()
    {
        var log = string.Join('\n',
            Line("2026-09-22 23:03:56", "trn-abc", "ep1", "Lelah-JW7", "5"),
            Line("2026-09-22 23:04:06", "trn-abc", "ep1", "Lelah-JW7", "24.53"),
            Line("2026-09-22 23:04:16", "trn-abc", "ep1", "Lelah-JW7", "5"));

        var incidents = CallQualityIncidentParser.Parse(log, Now);

        var i = Assert.Single(incidents);
        Assert.Equal("trn-abc", i.RoomName);
        Assert.Equal("Lelah-JW7", i.ParticipantLabel);
        Assert.Equal(3, i.EventCount);
        Assert.Equal(5, i.LowestKbps);
        Assert.Equal(new DateTime(2026, 9, 22, 17, 33, 56, DateTimeKind.Utc), i.FirstAtUtc); // 23:03:56 IST - 5:30
        Assert.Equal(new DateTime(2026, 9, 22, 17, 34, 16, DateTimeKind.Utc), i.LastAtUtc);
    }

    [Fact]
    public void A_rejoin_with_a_new_epId_is_a_separate_incident_even_in_the_same_room()
    {
        var log = string.Join('\n',
            Line("2026-09-22 23:03:56", "trn-abc", "ep1", "Lelah-JW7", "5"),
            Line("2026-09-22 23:08:03", "trn-abc", "ep2", "Lelah-JW7", "5"));

        var incidents = CallQualityIncidentParser.Parse(log, Now);

        Assert.Equal(2, incidents.Count);
        Assert.All(incidents, i => Assert.Equal(1, i.EventCount));
    }

    [Fact]
    public void Different_participants_in_different_rooms_are_separate_incidents_newest_first()
    {
        var log = string.Join('\n',
            Line("2026-09-22 23:03:56", "trn-a", "ep1", "A", "5"),
            Line("2026-09-23 04:53:29", "trn-b", "ep2", "B", "5"));

        var incidents = CallQualityIncidentParser.Parse(log, Now);

        Assert.Equal(new[] { "trn-b", "trn-a" }, incidents.Select(i => i.RoomName));
    }

    [Fact]
    public void Garbage_and_unrelated_lines_are_ignored_without_throwing()
    {
        var incidents = CallQualityIncidentParser.Parse("garbage\nJVB 2026-09-22 invoked for /x\n\n", Now);

        Assert.Empty(incidents);
    }

    [Fact]
    public void LooksSystemic_is_true_only_when_several_distinct_rooms_are_affected()
    {
        var narrow = new List<iucs.readernest.application.Dto.Monitoring.CallQualityIncidentDto>
        {
            new() { RoomName = "trn-a" },
            new() { RoomName = "trn-a" },
        };
        var broad = new List<iucs.readernest.application.Dto.Monitoring.CallQualityIncidentDto>
        {
            new() { RoomName = "trn-a" },
            new() { RoomName = "trn-b" },
            new() { RoomName = "trn-c" },
        };

        Assert.False(CallQualityIncidentParser.LooksSystemic(narrow));
        Assert.True(CallQualityIncidentParser.LooksSystemic(broad));
    }
}
