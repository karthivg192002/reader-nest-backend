using iucs.readernest.application.Dto.Admission;
using iucs.readernest.domain.Entities.Admission;

namespace iucs.readernest.application.Mappings
{
    public static class AdmissionMappings
    {
        public static DemoBookingDto ToDto(this DemoBooking booking)
        {
            return new DemoBookingDto
            {
                Id = booking.Id,
                ClassSessionId = booking.ClassSessionId,
                ParentName = booking.ParentName,
                ParentEmail = booking.ParentEmail,
                ParentPhone = booking.ParentPhone,
                ChildName = booking.ChildName,
                ChildAge = booking.ChildAge,
                Department = booking.Department,
                ConversionStatus = booking.ConversionStatus,
                FollowUpNotes = booking.FollowUpNotes,
                ScheduledStartAtUtc = booking.ClassSession?.ScheduledStartAtUtc,
                MeetingRoomId = booking.ClassSession?.MeetingRoomId,
                Participants = booking.Participants
                    .Select(p => new DemoParticipantDto { Name = p.Name, Email = p.Email, Phone = p.Phone })
                    .ToList(),
            };
        }
    }
}
