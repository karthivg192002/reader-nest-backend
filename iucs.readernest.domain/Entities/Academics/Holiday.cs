using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Academics
{
    /// <summary>Organisation-wide holiday shown on the colour-coded academic calendar.</summary>
    [Index(nameof(Date), IsUnique = true)]
    public class Holiday : AuditEntity
    {
        [MaxLength(150)]
        public string Name { get; set; } = null!;

        public DateOnly Date { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }
    }
}
