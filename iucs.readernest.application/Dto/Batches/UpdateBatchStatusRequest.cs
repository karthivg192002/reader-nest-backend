using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Batches
{
    public class UpdateBatchStatusRequest
    {
        [Required]
        public BatchStatus Status { get; set; }
    }
}
