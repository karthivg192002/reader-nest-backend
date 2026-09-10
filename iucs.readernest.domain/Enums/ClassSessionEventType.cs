namespace iucs.readernest.domain.Enums
{
    /// <summary>
    /// One entry in the append-only <see cref="Entities.Sessions.ClassSessionEventLog"/> trail —
    /// every join/leave/start/end/no-show/denial a live class (demo or regular) goes through.
    /// </summary>
    public enum ClassSessionEventType
    {
        /// <summary>The session's assigned teacher joins the live room for the first time — the
        /// real "class started" moment, distinct from the scheduled time.</summary>
        TeacherStarted,

        /// <summary>The teacher rejoins after already having started the class once (reconnect
        /// after a drop, or coming back after a "just leave for now").</summary>
        TeacherJoined,

        /// <summary>A student (via a parent account) joins the live room for the first time.</summary>
        StudentJoined,

        /// <summary>A student rejoins after already having joined once.</summary>
        StudentRejoined,

        /// <summary>A demo lead (no account yet) joins, matched by email against the booking.</summary>
        DemoParticipantJoined,

        /// <summary>Teacher deliberately leaves without ending the class ("Just leave for now"),
        /// or leaves after having already ended it — either way, an explicit exit.</summary>
        TeacherLeftClass,

        /// <summary>Student deliberately leaves the live room.</summary>
        StudentLeft,

        /// <summary>Teacher's connection drops without an explicit leave — network loss, browser
        /// crash/close. Unexpected unless the class had already been marked Completed.</summary>
        TeacherDisconnected,

        /// <summary>Student's connection drops without an explicit leave.</summary>
        StudentDisconnected,

        /// <summary>The definitive "End Class" action — SessionService.CompleteAsync.</summary>
        ClassEnded,

        /// <summary>Teacher never joined within the no-show grace window.</summary>
        TeacherNoShow,

        /// <summary>Student never joined within the no-show grace window.</summary>
        StudentNoShow,

        /// <summary>A join attempt was rejected — the caller isn't a genuine participant of
        /// this session.</summary>
        JoinDenied,
    }
}
