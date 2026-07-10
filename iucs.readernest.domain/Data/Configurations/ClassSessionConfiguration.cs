using iucs.readernest.domain.Entities.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace iucs.readernest.domain.Data.Configurations
{
    public class ClassSessionConfiguration : IEntityTypeConfiguration<ClassSession>
    {
        public void Configure(EntityTypeBuilder<ClassSession> builder)
        {
            // Two independent self-references; without explicit config EF pairs
            // them as inverse ends of a single one-to-one relationship.
            builder.HasOne(s => s.RescheduledFromSession)
                .WithMany()
                .HasForeignKey(s => s.RescheduledFromSessionId);

            builder.HasOne(s => s.CarriedForwardFromSession)
                .WithMany()
                .HasForeignKey(s => s.CarriedForwardFromSessionId);
        }
    }
}
