using iucs.readernest.application.Dto.Billing;
using iucs.readernest.application.Dto.Resources;
using iucs.readernest.domain.Entities.Billing;
using iucs.readernest.domain.Entities.Resources;
using iucs.readernest.domain.Entities.Users;

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
                ValidityDays = plan.ValidityDays,
                IsActive = plan.IsActive,
            };
        }

        /// <summary>
        /// Requires the invoice loaded with Child and Subscription.PackagePlan.Course included
        /// for ChildName/CourseName to resolve — both are display-only and stay null otherwise.
        /// </summary>
        public static InvoiceDto ToDto(this Invoice invoice)
        {
            return new InvoiceDto
            {
                Id = invoice.Id,
                InvoiceNumber = invoice.InvoiceNumber,
                ParentProfileId = invoice.ParentProfileId,
                ChildId = invoice.ChildId,
                ChildName = ResolveStudentName(invoice),
                CourseId = invoice.CourseId,
                CourseName = invoice.Course?.Name ?? invoice.Subscription?.PackagePlan?.Course?.Name,
                ParentName = invoice.ParentProfile?.User is null ? null : $"{invoice.ParentProfile.User.FirstName} {invoice.ParentProfile.User.LastName}".Trim(),
                ParentEmail = invoice.ParentProfile?.User?.Email,
                DepartmentId = invoice.DepartmentId,
                DepartmentName = invoice.Department?.Name ?? string.Empty,
                Amount = invoice.Amount,
                AmountPaid = invoice.AmountPaid,
                Currency = invoice.Currency,
                Status = invoice.Status,
                DueDate = invoice.DueDate,
                IssuedAtUtc = invoice.IssuedAtUtc,
                PaidAtUtc = invoice.PaidAtUtc,
            };
        }

        /// <summary>
        /// Display-only student name. Many invoices (family-level / manually created / admission)
        /// carry no ChildId, which left the All Invoices list blank. Falls back to the
        /// subscription's child, then to the parent's own children (joined when there are
        /// several). ChildId itself is untouched, so suspension/billing logic is unaffected.
        /// </summary>
        private static string? ResolveStudentName(Invoice invoice)
        {
            static string Full(Child c) => $"{c.FirstName} {c.LastName}".Trim();

            if (invoice.Child is { } child)
            {
                return Full(child);
            }

            if (invoice.Subscription?.Child is { } subscriptionChild)
            {
                return Full(subscriptionChild);
            }

            var siblings = invoice.ParentProfile?.Children;
            if (siblings is { Count: > 0 })
            {
                return string.Join(", ", siblings.OrderBy(c => c.FirstName).Select(Full));
            }

            return null;
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
                BatchName = resource.Batch?.Name,
                FolderId = resource.FolderId,
                IsDownloadable = resource.IsDownloadable,
                Description = resource.Description,
                CreatedAtUtc = resource.CreatedAtUtc,
            };
        }
    }
}
