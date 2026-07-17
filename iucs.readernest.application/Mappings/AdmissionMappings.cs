using iucs.readernest.application.Dto.Admission;
using iucs.readernest.domain.Entities.Admission;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Mappings
{
    public static class AdmissionMappings
    {
        /// <summary>Demo compensation structure (client-defined): ₹50 per demo, ₹100 once converted/enrolled.</summary>
        public const decimal NormalDemoFee = 50m;
        public const decimal ConvertedDemoFee = 100m;

        public static DemoBookingDto ToDto(this DemoBooking booking)
        {
            var teacher = booking.ClassSession?.TeacherProfile;
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
                TeacherProfileId = booking.ClassSession?.TeacherProfileId,
                TeacherName = teacher?.User is { } u ? $"{u.FirstName} {u.LastName}" : null,
                PayableAmount = booking.ConversionStatus == ConversionStatus.Enrolled ? ConvertedDemoFee : NormalDemoFee,
                Participants = booking.Participants
                    .Select(p => new DemoParticipantDto { Name = p.Name, Email = p.Email, Phone = p.Phone, IsChild = p.IsChild })
                    .ToList(),
            };
        }
    }
}
