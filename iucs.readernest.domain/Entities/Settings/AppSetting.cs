using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Settings
{
    /// <summary>
    /// Key/value organisation setting (branding, org details, notification toggles).
    /// Public rows (brand name, colours, logo) are served unauthenticated so the
    /// login screen can brand itself before any user signs in.
    /// </summary>
    [Index(nameof(Key), IsUnique = true)]
    public class AppSetting : AuditEntity
    {
        public SettingCategory Category { get; set; }

        [MaxLength(100)]
        public string Key { get; set; } = null!;

        [MaxLength(2000)]
        public string? Value { get; set; }

        public bool IsPublic { get; set; }
    }
}
