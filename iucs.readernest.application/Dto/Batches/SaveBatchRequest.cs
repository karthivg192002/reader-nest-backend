using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Batches
{
    /// <summary>Shared shape for create and update of a batch.</summary>
    public class SaveBatchRequest : IValidatableObject
    {
        [Required]
        public Guid CourseId { get; set; }

        [Required]
        public Guid TeacherProfileId { get; set; }

        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = null!;

        [Required]
        [Range(1, 500)]
        public int Capacity { get; set; }

        public DateOnly? StartDate { get; set; }

        public DateOnly? EndDate { get; set; }

        /// <summary>Overrides the course's own class length for this batch only. Null (the
        /// default) leaves the batch following the course's own duration.</summary>
        [Range(1, 500)]
        public int? DurationMinutesOverride { get; set; }

        /// <summary>How this batch's fee is expected to be collected.</summary>
        public BatchPaymentPlanType PaymentPlanType { get; set; } = BatchPaymentPlanType.FullPaymentDone;

        /// <summary>Required (and only meaningful) when PaymentPlanType is AfterSessions.</summary>
        [Range(1, 500)]
        public int? PaymentAfterSessionsCount { get; set; }

        /// <summary>Required (and only meaningful) when PaymentPlanType is DueOnDate.</summary>
        public DateOnly? PaymentDueDate { get; set; }

        /// <summary>Enforces the three payment options staying mutually exclusive server-side —
        /// the frontend already clears the other two fields on selection, but a caller hitting
        /// the API directly must not be able to save a batch with, say, both a session count and
        /// a due date set.</summary>
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            switch (PaymentPlanType)
            {
                case BatchPaymentPlanType.AfterSessions:
                    if (PaymentAfterSessionsCount is null)
                    {
                        yield return new ValidationResult(
                            "PaymentAfterSessionsCount is required when PaymentPlanType is AfterSessions.",
                            [nameof(PaymentAfterSessionsCount)]);
                    }
                    if (PaymentDueDate is not null)
                    {
                        yield return new ValidationResult(
                            "PaymentDueDate must be left unset when PaymentPlanType is AfterSessions.",
                            [nameof(PaymentDueDate)]);
                    }
                    break;

                case BatchPaymentPlanType.DueOnDate:
                    if (PaymentDueDate is null)
                    {
                        yield return new ValidationResult(
                            "PaymentDueDate is required when PaymentPlanType is DueOnDate.",
                            [nameof(PaymentDueDate)]);
                    }
                    if (PaymentAfterSessionsCount is not null)
                    {
                        yield return new ValidationResult(
                            "PaymentAfterSessionsCount must be left unset when PaymentPlanType is DueOnDate.",
                            [nameof(PaymentAfterSessionsCount)]);
                    }
                    break;

                default:
                    if (PaymentAfterSessionsCount is not null || PaymentDueDate is not null)
                    {
                        yield return new ValidationResult(
                            "PaymentAfterSessionsCount and PaymentDueDate must be left unset when PaymentPlanType is FullPaymentDone.",
                            [nameof(PaymentPlanType)]);
                    }
                    break;
            }
        }
    }
}
