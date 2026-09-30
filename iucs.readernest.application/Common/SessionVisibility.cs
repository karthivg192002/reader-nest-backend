using System.Linq.Expressions;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Common
{
    /// <summary>
    /// Sessions that only exist as history because something replaced them: the original of a
    /// single-class reschedule (status Rescheduled, its replacement is a new session), and the
    /// upcoming sessions a batch schedule edit cancelled before re-placing fresh ones. Staff,
    /// teacher and parent schedules all listed them next to their replacements, so a class moved
    /// from 5:30 to 6:00 showed twice and an MWF-to-TTS change left a wall of cancelled classes.
    /// Real cancellations (by a parent, a teacher's leave, staff) are deliberately NOT hidden --
    /// people need to see those and why.
    /// </summary>
    public static class SessionVisibility
    {
        /// <summary>CancellationReason stamped by SessionService.UpdateFutureScheduleAsync.</summary>
        public const string ScheduleAdjustedReason = "Schedule adjusted";

        /// <summary>Detail text of BatchService.CancelUpcomingSessionsAsync's reason -- the admin's
        /// "clear the upcoming classes and generate a new schedule" flow.</summary>
        public const string ScheduleRebuiltMarker = "cleared to rebuild its schedule";

        /// <summary>False for a superseded session -- apply to every schedule list.</summary>
        public static readonly Expression<Func<ClassSession, bool>> IsShownInSchedule = s =>
            s.Status != SessionStatus.Rescheduled
            && !(s.Status == SessionStatus.Cancelled
                 && s.CancellationReason != null
                 && (s.CancellationReason == ScheduleAdjustedReason || s.CancellationReason.Contains(ScheduleRebuiltMarker)));
    }
}
