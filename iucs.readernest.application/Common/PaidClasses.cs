using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Billing;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Common
{
    /// <summary>
    /// Client rule for fee suspension: a child is never suspended while even one class they have
    /// already paid for is still to come — only once the paid classes are used up.
    ///
    /// Paid classes, per (child, course): every non-cancelled invoice for that child and course
    /// contributes its class count (the plan's SessionsIncluded, else the course's TotalSessions)
    /// in proportion to how much of it is paid — a 24-class course half paid = 12 classes.
    /// Classes used: the child's batch sessions for that course that have Completed since they
    /// joined the batch. An invoice with no child or no course can't be measured in classes, so
    /// it is never exempt (the plain overdue rule applies).
    /// </summary>
    public static class PaidClasses
    {
        /// <summary>The subset of <paramref name="invoiceIds"/> whose child still has paid classes left for that invoice's course.</summary>
        public static async Task<HashSet<Guid>> WithClassesLeftAsync(
            IUnitOfWork unitOfWork, IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken = default)
        {
            var exempt = new HashSet<Guid>();
            if (invoiceIds.Count == 0)
            {
                return exempt;
            }

            var targets = (await unitOfWork.Repository<Invoice>().Query()
                    .Where(i => invoiceIds.Contains(i.Id) && i.ChildId != null)
                    .Select(i => new
                    {
                        i.Id,
                        ChildId = i.ChildId!.Value,
                        i.CourseId,
                        PlanCourseId = i.Subscription != null ? i.Subscription.PackagePlan.CourseId : null,
                    })
                    .ToListAsync(cancellationToken))
                .Select(t => new { t.Id, t.ChildId, CourseId = t.CourseId ?? t.PlanCourseId })
                .Where(t => t.CourseId.HasValue)
                .ToList();
            if (targets.Count == 0)
            {
                return exempt;
            }

            var childIds = targets.Select(t => t.ChildId).Distinct().ToList();

            // Paid classes: every invoice these children have, not just the overdue ones — an
            // earlier fully-paid term still has its classes to run.
            var invoices = await unitOfWork.Repository<Invoice>().Query()
                .Where(i => i.ChildId != null && childIds.Contains(i.ChildId.Value)
                    && i.Status != InvoiceStatus.Cancelled && i.Amount > 0)
                .Select(i => new
                {
                    ChildId = i.ChildId!.Value,
                    i.CourseId,
                    PlanCourseId = i.Subscription != null ? i.Subscription.PackagePlan.CourseId : null,
                    PlanSessions = i.Subscription != null ? i.Subscription.PackagePlan.SessionsIncluded : null,
                    CourseSessions = i.Course != null ? (int?)i.Course.TotalSessions : null,
                    PlanCourseSessions = i.Subscription != null && i.Subscription.PackagePlan.Course != null
                        ? (int?)i.Subscription.PackagePlan.Course.TotalSessions
                        : null,
                    i.Amount,
                    i.AmountPaid,
                })
                .ToListAsync(cancellationToken);

            var paidByChildCourse = new Dictionary<(Guid ChildId, Guid CourseId), decimal>();
            foreach (var invoice in invoices)
            {
                var courseId = invoice.CourseId ?? invoice.PlanCourseId;
                var sessions = invoice.PlanSessions ?? invoice.CourseSessions ?? invoice.PlanCourseSessions;
                if (courseId is null || sessions is not > 0)
                {
                    continue;
                }

                var paidShare = Math.Clamp(invoice.AmountPaid / invoice.Amount, 0m, 1m);
                var key = (invoice.ChildId, courseId.Value);
                paidByChildCourse[key] = paidByChildCourse.GetValueOrDefault(key) + sessions.Value * paidShare;
            }

            // Classes used: completed sessions of the child's batches for that course, counted
            // from when the child joined each batch (earlier sessions weren't theirs).
            var enrollments = await unitOfWork.Repository<BatchEnrollment>().Query()
                .Where(e => childIds.Contains(e.ChildId))
                .Select(e => new { e.ChildId, e.BatchId, e.Batch.CourseId, e.CreatedAtUtc })
                .ToListAsync(cancellationToken);
            var batchIds = enrollments.Select(e => e.BatchId).Distinct().ToList();
            var completed = await unitOfWork.Repository<ClassSession>().Query()
                .Where(s => s.BatchId != null && batchIds.Contains(s.BatchId.Value) && s.Status == SessionStatus.Completed)
                .Select(s => new { BatchId = s.BatchId!.Value, s.ScheduledStartAtUtc })
                .ToListAsync(cancellationToken);
            var completedByBatch = completed.ToLookup(s => s.BatchId, s => s.ScheduledStartAtUtc);

            foreach (var target in targets)
            {
                var paid = (int)Math.Floor(paidByChildCourse.GetValueOrDefault((target.ChildId, target.CourseId!.Value)));
                var used = enrollments
                    .Where(e => e.ChildId == target.ChildId && e.CourseId == target.CourseId)
                    .Sum(e => completedByBatch[e.BatchId].Count(start => start >= e.CreatedAtUtc));
                if (paid > used)
                {
                    exempt.Add(target.Id);
                }
            }

            return exempt;
        }
    }
}
