using iucs.readernest.application.Dto.Billing;
using iucs.readernest.application.Dto.Resources;
using iucs.readernest.domain.Entities.Billing;
using iucs.readernest.domain.Entities.Resources;

namespace iucs.readernest.application.Mappings
{
    public static class BillingMappings
    {
        public static PackagePlanDto ToDto(this PackagePlan plan)
        {
            return new PackagePlanDto
            {
                Id = plan.Id,
                Name = plan.Name,
                CourseId = plan.CourseId,
                BillingType = plan.BillingType,
                BillingCycle = plan.BillingCycle,
                Price = plan.Price,
                SessionsIncluded = plan.SessionsIncluded,
                IsActive = plan.IsActive,
            };
        }

        public static InvoiceDto ToDto(this Invoice invoice)
        {
            return new InvoiceDto
            {
                Id = invoice.Id,
                InvoiceNumber = invoice.InvoiceNumber,
                ParentProfileId = invoice.ParentProfileId,
                ChildId = invoice.ChildId,
                Department = invoice.Department,
                Amount = invoice.Amount,
                AmountPaid = invoice.AmountPaid,
                Currency = invoice.Currency,
                Status = invoice.Status,
                DueDate = invoice.DueDate,
                IssuedAtUtc = invoice.IssuedAtUtc,
                PaidAtUtc = invoice.PaidAtUtc,
            };
        }

        public static ResourceDto ToDto(this Resource resource)
        {
            return new ResourceDto
            {
                Id = resource.Id,
                Title = resource.Title,
                Type = resource.Type,
                MimeType = resource.MimeType,
                FileSizeBytes = resource.FileSizeBytes,
                CourseId = resource.CourseId,
                BatchId = resource.BatchId,
                IsDownloadable = resource.IsDownloadable,
                Description = resource.Description,
                CreatedAtUtc = resource.CreatedAtUtc,
            };
        }
    }
}
