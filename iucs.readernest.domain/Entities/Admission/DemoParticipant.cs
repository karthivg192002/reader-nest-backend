using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;

namespace iucs.readernest.domain.Entities.Admission
{
    /// <summary>
    /// Invited attendee of a demo class — extra parents/guardians or additional
    /// children (a demo can host more than two kids). Children carry no contact
    /// details of their own, so Email is optional.
    /// </summary>
    public class DemoParticipant : BaseEntity
    {
        public Guid DemoBookingId { get; set; }

        public DemoBooking DemoBooking { get; set; } = null!;

        [MaxLength(200)]
        public string Name { get; set; } = null!;

        [MaxLength(256)]
        public string? Email { get; set; }

        [MaxLength(20)]
        public string? Phone { get; set; }

        /// <summary>True for an additional child attending the demo (contact-less participant).</summary>
        public bool IsChild { get; set; }

        public bool HasJoined { get; set; }
    }
}
