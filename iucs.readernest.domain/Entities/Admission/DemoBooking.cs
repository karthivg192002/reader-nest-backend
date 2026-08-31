using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Billing;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Admission
{
    /// <summary>
    /// One-time demo class booking (never recurring) created by the admission team or
    /// the parent. Lead contact details live here because the parent may not have an
    /// account yet; the conversion funnel is tracked to Enrolled/NotInterested.
    /// </summary>
    [Index(nameof(ConversionStatus))]
    public class DemoBooking : AuditEntity
    {
        public Guid? ClassSessionId { get; set; }

        public ClassSession? ClassSession { get; set; }

        [MaxLength(200)]
        public string ParentName { get; set; } = null!;

        [MaxLength(256)]
        public string ParentEmail { get; set; } = null!;

        [MaxLength(20)]
        public string? ParentPhone { get; set; }

        [MaxLength(200)]
        public string ChildName { get; set; } = null!;

        public int? ChildAge { get; set; }

        public Guid? DepartmentId { get; set; }

        public Department? Department { get; set; }

        public ConversionStatus ConversionStatus { get; set; } = ConversionStatus.DemoScheduled;

        /// <summary>Most recently logged follow-up note's text — a quick-glance snapshot; the
        /// full dated/attributed history lives in the audit trail (see DemoBookingService's
        /// GetFollowUpNotesAsync/FollowUpAuditEntityName).</summary>
        [MaxLength(2000)]
        public string? FollowUpNotes { get; set; }

        /// <summary>Next time this lead should be followed up with, as set on the most recent
        /// follow-up note — powers the Leads table's "Next: …" column and reminders.</summary>
        public DateOnly? NextFollowUpOn { get; set; }

        /// <summary>Payment link shared with the parent; payment status is read from the linked invoice.</summary>
        [MaxLength(1000)]
        public string? PaymentLinkUrl { get; set; }

        public Guid? InvoiceId { get; set; }

        public Invoice? Invoice { get; set; }

        /// <summary>
        /// Join-based attendance capture for the primary parent (PDF's "System Marks
        /// Attendance", extended to demo leads): set the first time a signed-in account
        /// whose email matches <see cref="ParentEmail"/> joins this demo's classroom hub.
        /// A demo lead has no <see cref="Entities.Users.User"/>/<see cref="Entities.Users.Child"/>
        /// row to hang a <see cref="Sessions.SessionAttendance"/> entry off of (that table
        /// requires a real Child or TeacherProfile FK), so this lightweight field is the demo
        /// equivalent — mirrors <see cref="DemoParticipant.HasJoined"/> for additional invitees.
        /// </summary>
        public DateTime? ParentJoinedAtUtc { get; set; }

        public ICollection<DemoParticipant> Participants { get; set; } = new List<DemoParticipant>();
    }
}
