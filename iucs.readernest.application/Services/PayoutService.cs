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
        private static readonly int[] AllowedDurations = [30, 45, 60];

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
                .Include(r => r.TeacherProfile).ThenInclude(t => t.User);

            IQueryable<PayoutRate> filtered = query;
            if (teacherProfileId.HasValue)
            {
                filtered = filtered.Where(r => r.TeacherProfileId == teacherProfileId.Value);
            }

            var rates = await filtered
                .OrderBy(r => r.TeacherProfileId).ThenBy(r => r.DurationMinutes).ThenByDescending(r => r.EffectiveFrom)
                .ToListAsync(cancellationToken);
            return rates.Select(r => r.ToDto()).ToList();
        }

        public async Task<PayoutRateDto> SetRateAsync(SavePayoutRateRequest request, CancellationToken cancellationToken = default)
        {
            if (!AllowedDurations.Contains(request.DurationMinutes))
            {
                throw new DomainValidationException("Duration must be 30, 45 or 60 minutes.");
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

            // Same teacher/duration/effective-date updates in place; a new effective
            // date appends a row so past payouts stay reproducible.
            var rate = await _unitOfWork.Repository<PayoutRate>().FirstOrDefaultAsync(
                r => r.TeacherProfileId == request.TeacherProfileId
                     && r.DurationMinutes == request.DurationMinutes
                     && r.EffectiveFrom == request.EffectiveFrom,
                cancellationToken);

            if (rate is null)
            {
                rate = new PayoutRate
                {
                    TeacherProfileId = request.TeacherProfileId,
                    DurationMinutes = request.DurationMinutes,
                    EffectiveFrom = request.EffectiveFrom,
                };
                await _unitOfWork.Repository<PayoutRate>().AddAsync(rate, cancellationToken);
            }

            rate.RatePerSession = request.RatePerSession;
            rate.TeacherNoShowPenaltyPercent = request.TeacherNoShowPenaltyPercent;
            rate.IsActive = true;

            await _auditLog.StageAsync(AuditAction.Update, nameof(PayoutRate), rate.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var saved = await _unitOfWork.Repository<PayoutRate>().Query()
                .Include(r => r.TeacherProfile).ThenInclude(t => t.User)
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
                            && r.DurationMinutes == durationMinutes
                            && r.IsActive
                            && r.EffectiveFrom <= sessionDate)
                .OrderByDescending(r => r.EffectiveFrom)
                .FirstOrDefaultAsync(cancellationToken);

            rate ??= await _unitOfWork.Repository<PayoutRate>().Query()
                .Where(r => r.TeacherProfileId == null
                            && r.DurationMinutes == durationMinutes
                            && r.IsActive
                            && r.EffectiveFrom <= sessionDate)
                .OrderByDescending(r => r.EffectiveFrom)
                .FirstOrDefaultAsync(cancellationToken);

            var amount = type switch
            {
                PayoutItemType.SessionEarning => rate?.RatePerSession ?? 0m,
                PayoutItemType.StudentNoShowWaiting => rate?.RatePerSession ?? 0m,
                // The configured no-show penalty (WBS "Penalty configuration"): a percentage
                // of the session rate, so centres can deduct less, exactly, or more than
                // the missed session was worth.
                PayoutItemType.TeacherNoShowDeduction =>
                    -Math.Round((rate?.RatePerSession ?? 0m) * (rate?.TeacherNoShowPenaltyPercent ?? 100m) / 100m, 2),
                _ => 0m,
            };

            if (rate is null)
            {
                note = string.IsNullOrEmpty(note)
                    ? $"No payout rate configured for {durationMinutes}-minute sessions."
                    : $"{note} (no payout rate configured for {durationMinutes}-minute sessions)";
            }
            else if (type == PayoutItemType.TeacherNoShowDeduction && rate.TeacherNoShowPenaltyPercent != 100m)
            {
                note = $"{note} ({rate.TeacherNoShowPenaltyPercent:0.#}% of session rate)";
            }

            var payout = await GetOrCreateCurrentPayoutAsync(
                session.TeacherProfileId, session.ScheduledStartAtUtc, cancellationToken);

            payout.Items.Add(new PayoutItem
            {
                PayoutId = payout.Id,
                ClassSessionId = session.Id,
                Type = type,
                Amount = amount,
                Note = note,
            });
            payout.TotalAmount += amount;
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

            payout.Status = PayoutStatus.Finalized;
            payout.TotalAmount = items.Sum(i => i.Amount);
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
            // Load TRACKED (Query() is AsNoTracking): items added to an untracked payout
            // are silently dropped at SaveChanges — every accrual after the month's first
            // session would be lost. New items attach through the tracked parent.
            var payout = await _unitOfWork.Repository<Payout>().FirstOrDefaultAsync(
                p => p.TeacherProfileId == teacherProfileId
                     && p.PeriodYear == sessionStartUtc.Year
                     && p.PeriodMonth == sessionStartUtc.Month,
                cancellationToken);

            if (payout is not null)
            {
                if (payout.Status != PayoutStatus.Pending)
                {
                    throw new DomainValidationException(
                        $"The payout for {sessionStartUtc:yyyy-MM} is already {payout.Status} and cannot accrue new items.");
                }

                return payout;
            }

            payout = new Payout
            {
                TeacherProfileId = teacherProfileId,
                PeriodYear = sessionStartUtc.Year,
                PeriodMonth = sessionStartUtc.Month,
            };
            await _unitOfWork.Repository<Payout>().AddAsync(payout, cancellationToken);
            return payout;
        }

        private IQueryable<Payout> BaseQuery()
        {
            return _unitOfWork.Repository<Payout>().Query()
                .Include(p => p.Items)
                .Include(p => p.TeacherProfile).ThenInclude(t => t.User);
        }
    }
}
