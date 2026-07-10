using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.domain.Entities.Academics
{
    /// <summary>
    /// Mandatory first-login child enrollment form. Field list will be supplied by the
    /// client (open item in the sprint plan), so answers are stored as a JSON document
    /// rather than fixed columns. Admin can view/edit/approve/download submissions.
    /// </summary>
    public class EnrollmentForm : AuditEntity
    {
        public Guid ParentProfileId { get; set; }

        public ParentProfile ParentProfile { get; set; } = null!;

        /// <summary>Null until the form is approved and the child record is created/linked.</summary>
        public Guid? ChildId { get; set; }

        public Child? Child { get; set; }

        public string FormDataJson { get; set; } = "{}";

        public EnrollmentFormStatus Status { get; set; } = EnrollmentFormStatus.Pending;

        public DateTime? SubmittedAtUtc { get; set; }

        public Guid? ReviewedBy { get; set; }

        public DateTime? ReviewedAtUtc { get; set; }
    }
}
