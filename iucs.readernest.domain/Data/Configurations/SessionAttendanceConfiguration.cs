using iucs.readernest.domain.Entities.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace iucs.readernest.domain.Data.Configurations
{
    public class SessionAttendanceConfiguration : IEntityTypeConfiguration<SessionAttendance>
    {
        public void Configure(EntityTypeBuilder<SessionAttendance> builder)
        {
            // One live attendance row per participant per session — a rejoin after a
            // network drop must update the existing row, not insert a duplicate.
            builder.HasIndex(a => new { a.ClassSessionId, a.ChildId })
                .IsUnique()
                .HasFilter("\"ChildId\" IS NOT NULL AND \"IsDeleted\" = FALSE");

            builder.HasIndex(a => new { a.ClassSessionId, a.TeacherProfileId })
                .IsUnique()
                .HasFilter("\"TeacherProfileId\" IS NOT NULL AND \"IsDeleted\" = FALSE");
        }
    }
}
