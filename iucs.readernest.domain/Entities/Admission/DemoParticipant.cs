using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;

namespace iucs.readernest.domain.Entities.Admission
{
    /// <summary>
    /// Invited attendee of a demo class; demos are flexible for more than one parent to join.
    /// </summary>
    public class DemoParticipant : BaseEntity
    {
        public Guid DemoBookingId { get; set; }

        public DemoBooking DemoBooking { get; set; } = null!;

        [MaxLength(200)]
        public string Name { get; set; } = null!;

        [MaxLength(256)]
        public string Email { get; set; } = null!;

        [MaxLength(20)]
        public string? Phone { get; set; }

        public bool HasJoined { get; set; }
    }
}
