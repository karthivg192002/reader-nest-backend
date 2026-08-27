using iucs.readernest.application.Common;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Payouts;
using iucs.readernest.application.Mappings;
using iucs.readernest.domain.Entities.Payouts;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class PayoutService : IPayoutService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;
        private readonly INotificationService _notificationService;

        public PayoutService(IUnitOfWork unitOfWork, IAuditLogService auditLog, INotificationService notificationService)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
            _notificationService = notificationService;
        }

        public async Task<IReadOnlyList<PayoutRateDto>> ListRatesAsync(
            Guid? teacherProfileId,
            CancellationToken cancellationToken = default)
        {
            var query = _unitOfWork.Repository<PayoutRate>().Query()
                .Include(r => r.TeacherProfile).ThenInclude(t => t!.User);

            IQueryable<PayoutRate> filtered = query;
            if (teacherProfileId.HasValue)
            {
                filtered = filtered.Where(r => r.TeacherProfileId == teacherProfileId.Value);
            }

            var rates = await filtered
                .OrderBy(r => r.TeacherProfileId).ThenByDescending(r => r.EffectiveFrom)
                .ToListAsync(cancellationToken);
            return rates.Select(r => r.ToDto()).ToList();
        }

        public async Task<PayoutRateDto> SetRateAsync(SavePayoutRateRequest request, CancellationToken cancellationToken = default)
        {
            // A rate card drives real money with no downstream sanity check, so the bounds are
            // enforced here rather than trusted from the DTO. A negative rate makes every
            // completed class deduct from the teacher instead of paying them; a negative penalty
            // percent inverts the sign of the no-show deduction (-(rate * -100 / 100) = +rate),
            // silently turning a missed class into a bonus.
            if (request.RatePerMinute < 0)
            {
                throw new DomainValidationException("Rate per minute cannot be negative.");
            }

            // Deliberately NOT capped at 100: deducting more than the missed session was worth
            // is a supported policy (WBS p.31 "Penalty configuration" — centres can deduct less,
            // exactly, or more). Only the sign is wrong on its face, plus an upper bound loose
            // enough to allow any real policy while still catching a misplaced decimal point.
            if (request.TeacherNoShowPenaltyPercent is < 0 or > 1000)
            {
                throw new DomainValidationException(
                    "Teacher no-show penalty must be between 0 and 1000 percent of the session rate.");
            }

            // Null teacher = the centre-wide default card; only concrete teachers need to exist.
            if (request.TeacherProfileId is { } teacherProfileId)
            {
                var teacherExists = await _unitOfWork.Repository<TeacherProfile>()
                    .ExistsAsync(t => t.Id == teacherProfileId, cancellationToken);
                if (!teacherExists)
                {
                    throw new NotFoundException(nameof(TeacherProfile), teacherProfileId);
                }
            }

            // Same teacher/effective-date updates in place; a new effective date appends a
            // row so past payouts stay reproducible.
            var rate = await _unitOfWork.Repository<PayoutRate>().FirstOrDefaultAsync(
                r => r.TeacherProfileId == request.TeacherProfileId
                     && r.EffectiveFrom == request.EffectiveFrom,
                cancellationToken);

            if (rate is null)
            {
                rate = new PayoutRate
                {
                    TeacherProfileId = request.TeacherProfileId,
                    EffectiveFrom = request.EffectiveFrom,
                };
                await _unitOfWork.Repository<PayoutRate>().AddAsync(rate, cancellationToken);
            }

            rate.RatePerMinute = request.RatePerMinute;
            rate.TeacherNoShowPenaltyPercent = request.TeacherNoShowPenaltyPercent;
            rate.IsActive = true;

            await _auditLog.StageAsync(AuditAction.Update, nameof(PayoutRate), rate.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var saved = await _unitOfWork.Repository<PayoutRate>().Query()
                .Include(r => r.TeacherProfile).ThenInclude(t => t!.User)
                .FirstAsync(r => r.Id == rate.Id, cancellationToken);
            return saved.ToDto();
        }

        public async Task<IReadOnlyList<PayoutDto>> ListAsync(
            int? year,
            int? month,
            Guid? teacherProfileId,
            CancellationToken cancellationToken = default)
        {
            IQueryable<Payout> query = BaseQuery();

            if (year.HasValue)
            {
                query = query.Where(p => p.PeriodYear == year.Value);
            }

            if (month.HasValue)
            {
                query = query.Where(p => p.PeriodMonth == month.Value);
            }

            if (teacherProfileId.HasValue)
            {
                query = query.Where(p => p.TeacherProfileId == teacherProfileId.Value);
            }

            var payouts = await query
                .OrderByDescending(p => p.PeriodYear).ThenByDescending(p => p.PeriodMonth)
                .ToListAsync(cancellationToken);
            return payouts.Select(p => p.ToDto()).ToList();
        }

        public async Task<IReadOnlyList<PayoutDto>> ListForTeacherUserAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            var teacher = await _unitOfWork.Repository<TeacherProfile>()
                .FirstOrDefaultAsync(t => t.UserId == userId, cancellationToken)
                ?? throw new NotFoundException("No teacher profile is linked to the current account.");

            return await ListAsync(null, null, teacher.Id, cancellationToken);
        }

        public async Task AccrueForSessionAsync(
            ClassSession session,
            PayoutItemType type,
            string? note,
            CancellationToken cancellationToken = default)
        {
            var sessionDate = DateOnly.FromDateTime(session.ScheduledStartAtUtc);
            var durationMinutes = (int)Math.Round((session.ScheduledEndAtUtc - session.ScheduledStartAtUtc).TotalMinutes);

            // The rate effective on the session date: the teacher's own rate wins; teachers
            // without one are paid from the centre-wide default card (null teacher). Only
            // when neither exists does a zero item accrue, so the gap is visible on the
            // statement, never silent.
            var rate = await _unitOfWork.Repository<PayoutRate>().Query()
                .Where(r => r.TeacherProfileId == session.TeacherProfileId
                            && r.IsActive
                            && r.EffectiveFrom <= sessionDate)
                .OrderByDescending(r => r.EffectiveFrom)
                .FirstOrDefaultAsync(cancellationToken);

            rate ??= await _unitOfWork.Repository<PayoutRate>().Query()
                .Where(r => r.TeacherProfileId == null
                            && r.IsActive
                            && r.EffectiveFrom <= sessionDate)
                .OrderByDescending(r => r.EffectiveFrom)
                .FirstOrDefaultAsync(cancellationToken);

            // Priced off the scheduled duration, not the teacher's actual attendance --
            // a session's full rate is fixed the moment it's scheduled, so a dropped
            // connection or early finish doesn't shrink pay on its own (that's what
            // RequiresReview below is for; it flags the case for a human, never changes
            // the amount itself).
            var sessionRate = Math.Round((rate?.RatePerMinute ?? 0m) * durationMinutes, 2);
            var amount = type switch
            {
                PayoutItemType.SessionEarning => sessionRate,
                PayoutItemType.StudentNoShowWaiting => sessionRate,
                // The configured no-show penalty (WBS "Penalty configuration"): a percentage
                // of the session rate, so centres can deduct less, exactly, or more than
                // the missed session was worth.
                PayoutItemType.TeacherNoShowDeduction =>
                    -Math.Round(sessionRate * (rate?.TeacherNoShowPenaltyPercent ?? 100m) / 100m, 2),
                _ => 0m,
            };

            if (rate is null)
            {
                note = string.IsNullOrEmpty(note)
                    ? "No payout rate configured for this teacher."
                    : $"{note} (no payout rate configured for this teacher)";
            }
            else if (type == PayoutItemType.TeacherNoShowDeduction && rate.TeacherNoShowPenaltyPercent != 100m)
            {
                note = $"{note} ({rate.TeacherNoShowPenaltyPercent:0.#}% of session rate)";
            }

            // Full scheduled-duration pay still accrues even when the teacher's captured
            // attendance was much shorter than the class -- a dropped connection, a child
            // needing to stop early, and a teacher genuinely cutting the class short all look
            // identical from timestamps alone, and only a human reviewing the specific case can
            // tell them apart (see PayoutItem.RequiresReview's own doc comment). This only flags
            // for review; it never changes the amount itself.
            var requiresReview = false;
            if (type == PayoutItemType.SessionEarning && durationMinutes > 0)
            {
                var attendance = await _unitOfWork.Repository<SessionAttendance>().Query()
                    .Where(a => a.ClassSessionId == session.Id && a.TeacherProfileId == session.TeacherProfileId)
                    .FirstOrDefaultAsync(cancellationToken);

                if (attendance?.JoinedAtUtc is { } joinedAtUtc)
                {
                    // LeftAtUtc is only set once the teacher's hub connection actually
                    // disconnects, which for a self-completed class happens AFTER this call
                    // (Complete → then the page unmounts and drops the connection) -- so at this
                    // exact moment it is only populated when someone completes the class well
                    // after the teacher already left (e.g. an admin cleaning up later). Falling
                    // back to "now" correctly treats a still-connected teacher's own Complete
                    // click as the real end of their attendance.
                    var attendedEndUtc = attendance.LeftAtUtc ?? DateTime.UtcNow;
                    var attendedMinutes = (attendedEndUtc - joinedAtUtc).TotalMinutes;
                    var minFraction = await PayrollSettings.GetMinAttendanceFractionForReviewAsync(_unitOfWork, cancellationToken);
                    if (attendedMinutes < durationMinutes * minFraction)
                    {
                        requiresReview = true;
                        var attendedNote = $"Teacher attended only {Math.Max(0, attendedMinutes):0} of {durationMinutes} scheduled minutes -- review before finalizing.";
                        note = string.IsNullOrEmpty(note) ? attendedNote : $"{note} ({attendedNote})";
                    }
                }
                else
                {
                    // No SessionAttendance row at all (or one with no JoinedAtUtc) -- the
                    // platform has zero evidence the teacher ever actually joined. Completing a
                    // session doesn't require having joined the live classroom hub first (an
                    // admin, or the teacher via a direct API call, can mark it done regardless),
                    // so this is at least as worth a human's attention as attendance that fell
                    // short -- arguably more, since here there is no attendance at all to weigh.
                    requiresReview = true;
                    const string noAttendanceNote = "No attendance was ever recorded for the teacher on this session -- review before finalizing.";
                    note = string.IsNullOrEmpty(note) ? noAttendanceNote : $"{note} ({noAttendanceNote})";
                }
            }

            var payout = await GetOrCreateCurrentPayoutAsync(
                session.TeacherProfileId, session.ScheduledStartAtUtc, cancellationToken);

            if (payout.PeriodYear != sessionDate.Year || payout.PeriodMonth != sessionDate.Month)
            {
                var rolledNote = $"Rolled into {payout.PeriodYear}-{payout.PeriodMonth:D2}'s payout because {sessionDate:yyyy-MM}'s was already closed.";
                note = string.IsNullOrEmpty(note) ? rolledNote : $"{note} ({rolledNote})";
            }

            payout.Items.Add(new PayoutItem
            {
                PayoutId = payout.Id,
                ClassSessionId = session.Id,
                Type = type,
                Amount = amount,
                Note = note,
                RequiresReview = requiresReview,
            });
            payout.TotalAmount += amount;
        }

        public async Task<PayoutDto> AdjustItemAsync(
            Guid payoutId,
            Guid itemId,
            AdjustPayoutItemRequest request,
            CancellationToken cancellationToken = default)
        {
            // Load tracked (Query()/BaseQuery is AsNoTracking; mutating that never persists).
            var payout = await _unitOfWork.Repository<Payout>().FirstOrDefaultAsync(p => p.Id == payoutId, cancellationToken)
                ?? throw new NotFoundException(nameof(Payout), payoutId);

            if (payout.Status != PayoutStatus.Pending)
            {
                throw new DomainValidationException(
                    $"A payout in status '{payout.Status}' can no longer have its items adjusted.");
            }

            var item = await _unitOfWork.Repository<PayoutItem>().TrackedQuery()
                .FirstOrDefaultAsync(i => i.Id == itemId && i.PayoutId == payoutId, cancellationToken)
                ?? throw new NotFoundException(nameof(PayoutItem), itemId);

            var reason = request.Reason.Trim();
            if (reason.Length == 0)
            {
                throw new DomainValidationException("A reason is required to adjust a payout item.");
            }

            var delta = request.NewAmount - item.Amount;
            var adjustmentNote = $"Adjusted from {item.Amount:0.00} to {request.NewAmount:0.00}: {reason}";
            item.Note = string.IsNullOrEmpty(item.Note) ? adjustmentNote : $"{item.Note} ({adjustmentNote})";
            item.Amount = request.NewAmount;
            item.RequiresReview = false;
            _unitOfWork.Repository<PayoutItem>().Update(item);

            payout.TotalAmount += delta;
            _unitOfWork.Repository<Payout>().Update(payout);

            await _auditLog.StageAsync(AuditAction.Update, nameof(PayoutItem), item.Id.ToString(),
                changesJson: $"{{\"reason\":\"{reason}\",\"delta\":{delta}}}", cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return (await BaseQuery().FirstAsync(p => p.Id == payoutId, cancellationToken)).ToDto();
        }

        public async Task<PayoutDto> FinalizeAsync(Guid payoutId, CancellationToken cancellationToken = default)
        {
            // Load tracked (Query()/BaseQuery is AsNoTracking; mutating that never persists).
            var payout = await _unitOfWork.Repository<Payout>().FirstOrDefaultAsync(p => p.Id == payoutId, cancellationToken)
                ?? throw new NotFoundException(nameof(Payout), payoutId);

            if (payout.Status != PayoutStatus.Pending)
            {
                throw new DomainValidationException($"A payout in status '{payout.Status}' cannot be finalized.");
            }

            var items = await _unitOfWork.Repository<PayoutItem>().Query()
                .Where(i => i.PayoutId == payoutId)
                .ToListAsync(cancellationToken);

            // A flag nobody is forced to look at is decoration, not a safeguard. AdjustItemAsync
            // clears RequiresReview whether or not the amount actually changes, so "reviewed, full
            // amount stands" is a real, one-line-noted admin decision, not this check being worked
            // around.
            if (items.Any(i => i.RequiresReview))
            {
                throw new DomainValidationException(
                    "This payout has item(s) still flagged for review (teacher attendance fell well short of the scheduled class). Adjust or confirm each one before finalizing.");
            }

            payout.Status = PayoutStatus.Finalized;
            // Floored at zero: TeacherNoShowPenaltyPercent is deliberately allowed up to 1000%
            // (SetRateAsync's own comment — "centres can deduct... more than the missed session
            // was worth"), so a teacher whose only accrued item this period is one heavily
            // penalized no-show can otherwise finalize to a genuinely negative total. Nothing
            // downstream expects that: this exact value is the "Total" token in the
            // payout-statement email below and the salary-slip email MarkPaidAsync sends later,
            // so an unfloored negative total would be emailed to the teacher as if it meant
            // "you owe us money" — never the intent of a deduction, which should read as "you
            // earned nothing this period," not a debt.
            payout.TotalAmount = Math.Max(0m, items.Sum(i => i.Amount));
            payout.FinalizedAtUtc = DateTime.UtcNow;

            await _auditLog.StageAsync(AuditAction.Update, nameof(Payout), payout.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Monthly statement dispatch — the notification row records delivery state
            var teacherUser = await TeacherUserAsync(payout.TeacherProfileId, cancellationToken);
            var period = $"{payout.PeriodYear}-{payout.PeriodMonth:D2}";
            var lines = items
                .Select(i => $"- {i.Type}: {i.Amount:0.00}{(string.IsNullOrEmpty(i.Note) ? "" : $" ({i.Note})")}");
            await _notificationService.SendTemplatedEmailAsync(
                teacherUser.Id,
                teacherUser.Email,
                NotificationType.PayoutStatement,
                "payout-statement",
                new Dictionary<string, string>
                {
                    ["TeacherFirstName"] = teacherUser.FirstName,
                    ["Period"] = period,
                    ["LinesText"] = string.Join("\n", lines),
                    ["Total"] = payout.TotalAmount.ToString("0.00"),
                },
                cancellationToken);

            payout.EmailSentAtUtc = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return (await BaseQuery().FirstAsync(p => p.Id == payoutId, cancellationToken)).ToDto();
        }

        public async Task<PayoutDto> MarkPaidAsync(Guid payoutId, CancellationToken cancellationToken = default)
        {
            // Load tracked (Query()/BaseQuery is AsNoTracking; mutating that never persists).
            var payout = await _unitOfWork.Repository<Payout>().FirstOrDefaultAsync(p => p.Id == payoutId, cancellationToken)
                ?? throw new NotFoundException(nameof(Payout), payoutId);

            if (payout.Status != PayoutStatus.Finalized)
            {
                throw new DomainValidationException("Only a finalized payout can be marked as paid.");
            }

            payout.Status = PayoutStatus.Paid;

            await _auditLog.StageAsync(AuditAction.Update, nameof(Payout), payout.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Salary slip: emailed automatically the moment the payment is processed.
            var items = await _unitOfWork.Repository<PayoutItem>().Query()
                .Where(i => i.PayoutId == payoutId)
                .ToListAsync(cancellationToken);
            var teacherUser = await TeacherUserAsync(payout.TeacherProfileId, cancellationToken);
            var period = $"{payout.PeriodYear}-{payout.PeriodMonth:D2}";
            var slipLines = items
                .Select(i => $"  {i.Type,-26} {i.Amount,12:0.00}{(string.IsNullOrEmpty(i.Note) ? "" : $"   {i.Note}")}");
            var slip =
                $"Paid on: {DateTime.UtcNow:yyyy-MM-dd}\n\n" +
                $"Earnings & adjustments\n{string.Join("\n", slipLines)}";
            await _notificationService.SendTemplatedEmailAsync(
                teacherUser.Id,
                teacherUser.Email,
                NotificationType.PayoutStatement,
                "salary-slip",
                new Dictionary<string, string>
                {
                    ["TeacherFirstName"] = teacherUser.FirstName,
                    ["Period"] = period,
                    ["SlipBody"] = slip,
                    ["Total"] = payout.TotalAmount.ToString("0.00"),
                },
                cancellationToken);

            return (await BaseQuery().FirstAsync(p => p.Id == payoutId, cancellationToken)).ToDto();
        }

        private async Task<User> TeacherUserAsync(Guid teacherProfileId, CancellationToken cancellationToken)
        {
            return await _unitOfWork.Repository<TeacherProfile>().Query()
                .Where(t => t.Id == teacherProfileId)
                .Select(t => t.User)
                .FirstAsync(cancellationToken);
        }

        private async Task<Payout> GetOrCreateCurrentPayoutAsync(
            Guid teacherProfileId,
            DateTime sessionStartUtc,
            CancellationToken cancellationToken)
        {
            // Session completion (and no-show marking) must never hard-fail just because
            // payroll already ran for this month — that would leave the class permanently
            // un-completable. Roll the late item forward into the next open (Pending, or not
            // yet created) payout period; a closed period's own total is never reopened or
            // mutated. Bounded so a pathological run of pre-finalized months can't hang the
            // request — in real usage this resolves on the first or second hop.
            var period = new DateTime(sessionStartUtc.Year, sessionStartUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            for (var hop = 0; hop < 24; hop++)
            {
                var payout = await FindPayoutForPeriodAsync(teacherProfileId, period.Year, period.Month, cancellationToken);
                if (payout is null)
                {
                    return await CreatePayoutAsync(teacherProfileId, period.Year, period.Month, cancellationToken);
                }

                if (payout.Status == PayoutStatus.Pending)
                {
                    return payout;
                }

                period = period.AddMonths(1);
            }

            throw new DomainValidationException(
                $"No open payout period found for {sessionStartUtc:yyyy-MM} within 24 months forward. " +
                "Reopen one of the closed periods first.");
        }

        // Load TRACKED (Query() is AsNoTracking): items added to an untracked payout
        // are silently dropped at SaveChanges — every accrual after the month's first
        // session would be lost. New items attach through the tracked parent.
        private async Task<Payout?> FindPayoutForPeriodAsync(
            Guid teacherProfileId, int year, int month, CancellationToken cancellationToken)
        {
            return await _unitOfWork.Repository<Payout>().FirstOrDefaultAsync(
                p => p.TeacherProfileId == teacherProfileId && p.PeriodYear == year && p.PeriodMonth == month,
                cancellationToken);
        }

        private async Task<Payout> CreatePayoutAsync(
            Guid teacherProfileId, int year, int month, CancellationToken cancellationToken)
        {
            var payout = new Payout { TeacherProfileId = teacherProfileId, PeriodYear = year, PeriodMonth = month };
            await _unitOfWork.Repository<Payout>().AddAsync(payout, cancellationToken);
            return payout;
        }

        private IQueryable<Payout> BaseQuery()
        {
            return _unitOfWork.Repository<Payout>().Query()
                .Include(p => p.Items).ThenInclude(i => i.ClassSession).ThenInclude(cs => cs!.Batch)
                .Include(p => p.TeacherProfile).ThenInclude(t => t.User);
        }
    }
}
