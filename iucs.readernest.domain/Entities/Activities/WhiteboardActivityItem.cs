using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Activities
{
    /// <summary>
    /// One piece on a <see cref="WhiteboardActivityTemplate"/>'s board. <see cref="Label"/>
    /// is what DragDrop/TagMatch match this emoji against (a letter, a number, a word — the
    /// template author's choice); <see cref="IsTarget"/> is what Hotspot mode clicks for —
    /// each mode reads only the field it needs, same "one row shape, mode-specific meaning"
    /// choice as keeping this a single table rather than three near-identical ones.
    /// </summary>
    [Index(nameof(WhiteboardActivityTemplateId))]
    public class WhiteboardActivityItem : BaseEntity
    {
        public Guid WhiteboardActivityTemplateId { get; set; }

        public WhiteboardActivityTemplate WhiteboardActivityTemplate { get; set; } = null!;

        [MaxLength(16)]
        public string Emoji { get; set; } = null!;

        /// <summary>Match text for DragDrop/TagMatch (e.g. a letter or number). Unused by Hotspot.</summary>
        [MaxLength(20)]
        public string? Label { get; set; }

        /// <summary>Whether this piece counts as a correct find in Hotspot mode. Unused otherwise.</summary>
        public bool IsTarget { get; set; }

        public int DisplayOrder { get; set; }
    }
}
