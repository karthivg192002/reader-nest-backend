using System.ComponentModel.DataAnnotations;
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

        public Department? Department { get; set; }

        public ConversionStatus ConversionStatus { get; set; } = ConversionStatus.DemoScheduled;

        [MaxLength(2000)]
        public string? FollowUpNotes { get; set; }

        /// <summary>Payment link shared with the parent; payment status is read from the linked invoice.</summary>
        [MaxLength(1000)]
        public string? PaymentLinkUrl { get; set; }

        public Guid? InvoiceId { get; set; }

        public Invoice? Invoice { get; set; }

        public ICollection<DemoParticipant> Participants { get; set; } = new List<DemoParticipant>();
    }
}
