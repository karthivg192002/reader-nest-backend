using iucs.readernest.domain.Entities.Navigation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace iucs.readernest.domain.Data.Configurations
{
    public class MenuPermissionConfiguration : IEntityTypeConfiguration<MenuPermission>
    {
        public void Configure(EntityTypeBuilder<MenuPermission> builder)
        {
            // A grant is either role-level or (reserved for later) user-level, never neither —
            // the two composite unique indexes on the entity don't by themselves rule out a row
            // with both FKs null, so this is the DB-enforced backstop for that invariant.
            builder.ToTable(t => t.HasCheckConstraint(
                "ck_menu_permissions_owner",
                "role_definition_id IS NOT NULL OR user_id IS NOT NULL"));
        }
    }
}
