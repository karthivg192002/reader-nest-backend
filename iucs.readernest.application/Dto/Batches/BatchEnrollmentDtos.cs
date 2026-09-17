using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Batches
{
    /// <summary>A child currently placed in a batch — the "Assign Students" roster (WBS p.17/25).</summary>
    public class BatchStudentDto
    {
        public Guid EnrollmentId { get; set; }

        public Guid ChildId { get; set; }

        public string ChildName { get; set; } = null!;

        public string? AcademicLevel { get; set; }

        public EnrollmentStatus Status { get; set; }

        public DateTime EnrolledAtUtc { get; set; }
    }

    /// <summary>An active, approved child not yet placed in this specific batch — candidates for the "Assign students" picker.</summary>
    public class UnassignedChildDto
    {
        public Guid ChildId { get; set; }

        public string ChildName { get; set; } = null!;

        public string ParentName { get; set; } = null!;

        /// <summary>Lets the "Assign students" picker tell apart two same-named children (e.g. real duplicates from migration, or one child enrolled separately per course) without an extra lookup.</summary>
        public string? ParentEmail { get; set; }

        public string? AcademicLevel { get; set; }

        /// <summary>Course name(s) this child is currently actively enrolled in elsewhere, comma-joined; null if not enrolled anywhere yet.</summary>
        public string? CurrentCourses { get; set; }
    }

    public class AssignStudentRequest
    {
        [Required]
        public Guid ChildId { get; set; }
    }

    /// <summary>
    /// One row of a teacher's "My Students" page (ListMyStudentsAsync) -- one per active
    /// enrollment across every batch this teacher is assigned to. Teacher feedback: "the
    /// names and list of students were clearly visible separately [in the previous LMS],
    /// which made it very easy for us to track the students we were currently handling...
    /// how many sessions were remaining for each student and identify if a child was not
    /// attending regularly." Nothing in the app aggregated this before -- a teacher's own
    /// roster only ever existed implicitly, scattered across every batch's own session list.
    /// </summary>
    public class TeacherStudentDto
    {
        public Guid ChildId { get; set; }

        public string ChildName { get; set; } = null!;

        public string ParentName { get; set; } = null!;

        public string? AcademicLevel { get; set; }

        public Guid BatchId { get; set; }

        public string BatchName { get; set; } = null!;

        public string CourseName { get; set; } = null!;

        public DateTime EnrolledAtUtc { get; set; }

        /// <summary>The course's own full curriculum length -- not how many are left to
        /// *schedule* (a batch may not have its whole schedule generated yet), how many of
        /// them this batch has actually delivered so far.</summary>
        public int TotalSessions { get; set; }

        /// <summary>How many of this batch's sessions (not just since this child enrolled --
        /// the course's own progress) have already run.</summary>
        public int CompletedSessions { get; set; }

        /// <summary>TotalSessions - CompletedSessions, floored at 0 -- 0 doesn't distinguish
        /// "course finished" from "schedule not fully generated yet", same ambiguity the
        /// batch's own remaining-session tracking already has elsewhere in the app.</summary>
        public int RemainingSessions { get; set; }

        /// <summary>How many of this batch's sessions have run since this specific child
        /// enrolled -- the real denominator for judging attendance (a child who joined
        /// halfway through shouldn't be judged against sessions before they existed here).</summary>
        public int SessionsSinceEnrollment { get; set; }

        /// <summary>Present-status attendance rows for this child against SessionsSinceEnrollment.</summary>
        public int AttendedCount { get; set; }

        public DateTime? LastAttendedAtUtc { get; set; }
    }
}
