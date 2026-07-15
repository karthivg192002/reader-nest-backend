using System.Linq.Expressions;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Admission;
using iucs.readernest.domain.Entities.Auditing;
using iucs.readernest.domain.Entities.Billing;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Communication;
using iucs.readernest.domain.Entities.Integrations;
using iucs.readernest.domain.Entities.Navigation;
using iucs.readernest.domain.Entities.Payouts;
using iucs.readernest.domain.Entities.Resources;
using iucs.readernest.domain.Entities.Settings;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.NameTranslation;

namespace iucs.readernest.domain.Data
{
    public class ReaderNestDbContext : DbContext
    {
        public ReaderNestDbContext(DbContextOptions<ReaderNestDbContext> options)
            : base(options)
        {
        }

        // Users & access control
        public DbSet<User> Users => Set<User>();
        public DbSet<ParentProfile> ParentProfiles => Set<ParentProfile>();
        public DbSet<TeacherProfile> TeacherProfiles => Set<TeacherProfile>();
        public DbSet<Child> Children => Set<Child>();
        public DbSet<SubAdminPermission> SubAdminPermissions => Set<SubAdminPermission>();
        public DbSet<RoleDefinition> RoleDefinitions => Set<RoleDefinition>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

        // Academics
        public DbSet<CourseCategory> CourseCategories => Set<CourseCategory>();
        public DbSet<Course> Courses => Set<Course>();
        public DbSet<Batch> Batches => Set<Batch>();
        public DbSet<BatchEnrollment> BatchEnrollments => Set<BatchEnrollment>();
        public DbSet<EnrollmentForm> EnrollmentForms => Set<EnrollmentForm>();
        public DbSet<Holiday> Holidays => Set<Holiday>();
        public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();

        // Sessions
        public DbSet<ClassSession> ClassSessions => Set<ClassSession>();
        public DbSet<SessionAttendance> SessionAttendances => Set<SessionAttendance>();
        public DbSet<SessionRecording> SessionRecordings => Set<SessionRecording>();
        public DbSet<EngagementEvent> EngagementEvents => Set<EngagementEvent>();
        public DbSet<StudentAward> StudentAwards => Set<StudentAward>();

        // Admission
        public DbSet<DemoBooking> DemoBookings => Set<DemoBooking>();
        public DbSet<DemoParticipant> DemoParticipants => Set<DemoParticipant>();
        public DbSet<DemoFeedback> DemoFeedbacks => Set<DemoFeedback>();

        // Billing
        public DbSet<PaymentAccount> PaymentAccounts => Set<PaymentAccount>();
        public DbSet<PackagePlan> PackagePlans => Set<PackagePlan>();
        public DbSet<Subscription> Subscriptions => Set<Subscription>();
        public DbSet<Invoice> Invoices => Set<Invoice>();
        public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
        public DbSet<Refund> Refunds => Set<Refund>();
        public DbSet<FeeSuspension> FeeSuspensions => Set<FeeSuspension>();

        // Payouts
        public DbSet<PayoutRate> PayoutRates => Set<PayoutRate>();
        public DbSet<Payout> Payouts => Set<Payout>();
        public DbSet<PayoutItem> PayoutItems => Set<PayoutItem>();

        // Resources
        public DbSet<Resource> Resources => Set<Resource>();
        public DbSet<ResourceAccess> ResourceAccesses => Set<ResourceAccess>();

        // Communication & auditing
        public DbSet<Notification> Notifications => Set<Notification>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

        // Platform configuration
        public DbSet<AppSetting> AppSettings => Set<AppSetting>();
        public DbSet<MenuItem> MenuItems => Set<MenuItem>();
        public DbSet<Integration> Integrations => Set<Integration>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.Properties<decimal>().HavePrecision(12, 2);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReaderNestDbContext).Assembly);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                // Persist enums as readable strings so the schema is self-documenting
                foreach (var property in entityType.GetProperties())
                {
                    var clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                    if (clrType.IsEnum)
                    {
                        var converterType = typeof(EnumToStringConverter<>).MakeGenericType(clrType);
                        property.SetValueConverter((ValueConverter)Activator.CreateInstance(converterType)!);
                        property.SetMaxLength(64);
                    }
                }

                // Restrict by default: deletes must never cascade through academic or financial history
                foreach (var foreignKey in entityType.GetForeignKeys())
                {
                    foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
                }

                if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType) && entityType.BaseType is null)
                {
                    // Soft-delete filter applied uniformly to every BaseEntity-derived table
                    var parameter = Expression.Parameter(entityType.ClrType, "e");
                    var body = Expression.Not(Expression.Property(parameter, nameof(BaseEntity.IsDeleted)));
                    modelBuilder.Entity(entityType.ClrType).HasQueryFilter(Expression.Lambda(body, parameter));

                    // Unique keys must only bind live rows, otherwise a soft-deleted row
                    // (e.g. a deleted user's email) blocks re-creation forever
                    foreach (var index in entityType.GetIndexes())
                    {
                        if (index.IsUnique && index.GetFilter() is null)
                        {
                            // Raw SQL: must use the final snake_case column name,
                            // SnakeCaseDatabase cannot rewrite filter strings
                            index.SetFilter("\"is_deleted\" = FALSE");
                        }
                    }
                }
            }

            SnakeCaseDatabase(modelBuilder);
        }

        #region Private Method
        private void SnakeCaseDatabase(ModelBuilder modelBuilder)
        {
            var mapper = new NpgsqlSnakeCaseNameTranslator();
            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                entity.SetTableName(mapper.TranslateMemberName(entity.GetTableName()));

                foreach (var property in entity.GetProperties())
                {
                    property.SetColumnName(mapper.TranslateMemberName(property.Name));
                }

                foreach (var key in entity.GetKeys())
                {
                    key.SetName(mapper.TranslateMemberName(key.GetName()));
                }

                foreach (var fk in entity.GetForeignKeys())
                {
                    fk.SetConstraintName(mapper.TranslateMemberName(fk.GetConstraintName()));
                }

                foreach (var index in entity.GetIndexes())
                {
                    index.SetDatabaseName(mapper.TranslateMemberName(index.GetDatabaseName()));
                }
            }
        }
        #endregion
    }
}
