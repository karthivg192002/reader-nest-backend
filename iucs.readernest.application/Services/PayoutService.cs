using iucs.readernest.application.Common;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Payouts;
using iucs.readernest.application.Helper;
using iucs.readernest.application.Mappings;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Admission;
using iucs.readernest.domain.Entities.Payouts;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace iucs.readernest.application.Services
{
    public class PayoutService : IPayoutService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;
        private readonly INotificationService _notificationService;
        private readonly IConfiguration _configuration;

        public PayoutService(
            IUnitOfWork unitOfWork,
            IAuditLogService auditLog,
            INotificationService notificationService,
            IConfiguration configuration)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
            _notificationService = notificationService;
            _configuration = configuration;
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

        public async Task<PayoutItem> AccrueForSessionAsync(
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
            int? scheduledMinutes = null;
            int? deliveredMinutes = null;
            if (type == PayoutItemType.SessionEarning && durationMinutes > 0)
            {
                scheduledMinutes = durationMinutes;
                var attendance = await _unitOfWork.Repository<SessionAttendance>().Query()
                    .Where(a => a.ClassSessionId == session.Id && a.TeacherProfileId == session.TeacherProfileId)
                    .FirstOrDefaultAsync(cancellationToken);

                if (attendance?.JoinedAtUtc is { } joinedAtUtc)
                {
                    deliveredMinutes = DeliveredMinutes(session, joinedAtUtc, attendance.LeftAtUtc ?? DateTime.UtcNow);

                    // Core rule (client requirement): ANY class that ran for less than its
                    // scheduled duration is flagged for payout approval — not only ones under
                    // some percentage threshold, and never silently treated as a normal
                    // completed class. Whole minutes, so a class that started a few seconds late
                    // doesn't trip it.
                    if (deliveredMinutes < durationMinutes)
                    {
                        requiresReview = true;
                        var shortNote = $"Class ran {deliveredMinutes} of {durationMinutes} scheduled minutes ({durationMinutes - deliveredMinutes} min short) — awaiting payout approval.";
                        note = string.IsNullOrEmpty(note) ? shortNote : $"{note} ({shortNote})";
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
                    const string noAttendanceNote = "No attendance was ever recorded for the teacher on this session — review before finalizing.";
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

            var item = new PayoutItem
            {
                PayoutId = payout.Id,
                ClassSessionId = session.Id,
                Type = type,
                Amount = amount,
                Note = note,
                RequiresReview = requiresReview,
                ScheduledMinutes = scheduledMinutes,
                DeliveredMinutes = deliveredMinutes,
            };
            payout.Items.Add(item);
            payout.TotalAmount += amount;
            return item;
        }

        /// <summary>
        /// Whole minutes the teacher was in class, counted inside the scheduled window: time
        /// after the scheduled end never counts, and time before the scheduled start doesn't
        /// either once the class window has actually begun (a teacher who joins at 2:58 for a
        /// 3:00–3:30 class and leaves at 3:28 delivered 28 minutes, not 30). The start clip only
        /// applies when the window began before the teacher left, so a class completed ahead of
        /// its slot (an ad-hoc/rescheduled run) is still measured by the time actually spent.
        /// </summary>
        public static int DeliveredMinutes(ClassSession session, DateTime joinedAtUtc, DateTime leftAtUtc)
        {
            var end = leftAtUtc < session.ScheduledEndAtUtc ? leftAtUtc : session.ScheduledEndAtUtc;
            var start = joinedAtUtc < session.ScheduledStartAtUtc && session.ScheduledStartAtUtc < leftAtUtc
                ? session.ScheduledStartAtUtc
                : joinedAtUtc;
            var minutes = (end - start).TotalMinutes;
            return minutes <= 0 ? 0 : (int)Math.Round(minutes, MidpointRounding.AwayFromZero);
        }

        public async Task NotifyPayoutReviewAsync(Guid payoutItemId, CancellationToken cancellationToken = default)
        {
            var approval = (await ApprovalQueryAsync(i => i.Id == payoutItemId, cancellationToken)).FirstOrDefault();
            if (approval is null)
            {
                return;
            }

            // Admin plus every Sub Admin who can approve payouts — the Management / Founder
            // presets carry Payouts:Approve as a required grant (RequiredSystemRolePermissions).
            var module = PermissionModule.Payouts.ToString();
            var approvers = _unitOfWork.Repository<SubAdminPermission>().Query();
            var recipients = await _unitOfWork.Repository<User>().Query()
                .Include(u => u.RoleDefinition)
                .Where(u => u.Status == UserStatus.Active
                            && (u.Role == UserRole.Admin
                                || (u.Role == UserRole.SubAdmin
                                    && approvers.Any(p => p.UserId == u.Id && p.Module == module && p.CanApprove))))
                .ToListAsync(cancellationToken);

            var baseUrl = (_configuration["Frontend:BaseUrl"] ?? "http://localhost:5173").TrimEnd('/');
            foreach (var recipient in recipients)
            {
                var portal = recipient.Role == UserRole.Admin
                    ? "/admin"
                    : (recipient.RoleDefinition?.DefaultRoute ?? "/management").TrimEnd('/');
                var tokens = new Dictionary<string, string>
                {
                    ["TeacherName"] = approval.TeacherName,
                    ["ClassName"] = approval.ClassName,
                    ["Students"] = string.IsNullOrWhiteSpace(approval.StudentNames) ? "—" : approval.StudentNames,
                    ["ScheduledTime"] = approval.ScheduledStartAtUtc is { } s && approval.ScheduledEndAtUtc is { } e
                        ? DateTimeDisplay.ToLocalRange(s, e, recipient.TimeZoneId)
                        : "—",
                    ["ScheduledMinutes"] = approval.ScheduledMinutes?.ToString() ?? "—",
                    ["ActualMinutes"] = approval.DeliveredMinutes?.ToString() ?? "No attendance recorded",
                    ["ShortfallMinutes"] = approval.ShortfallMinutes?.ToString() ?? "—",
                    ["ApprovalsUrl"] = $"{baseUrl}{portal}/payout-approvals",
                };
                try
                {
                    await _notificationService.SendTemplatedEmailAsync(
                        recipient.Id, recipient.Email, NotificationType.General, "short-class-payout-approval", tokens, cancellationToken);
                }
                catch (Exception)
                {
                    // Best-effort: the item is already saved and listed under Payout Approvals,
                    // so one failed alert must not stop the rest or fail the class completion.
                }
            }
        }

        public async Task<IReadOnlyList<PayoutApprovalDto>> ListApprovalsAsync(bool pending, CancellationToken cancellationToken = default)
        {
            return pending
                ? await ApprovalQueryAsync(i => i.RequiresReview, cancellationToken)
                : await ApprovalQueryAsync(i => i.ReviewDecision != null, cancellationToken);
        }

        public async Task<PayoutApprovalDto> DecideApprovalAsync(
            Guid payoutItemId,
            Guid reviewerUserId,
            DecidePayoutApprovalRequest request,
            CancellationToken cancellationToken = default)
        {
            var item = await _unitOfWork.Repository<PayoutItem>().TrackedQuery()
                .Include(i => i.Payout)
                .FirstOrDefaultAsync(i => i.Id == payoutItemId, cancellationToken)
                ?? throw new NotFoundException(nameof(PayoutItem), payoutItemId);

            if (!item.RequiresReview)
            {
                throw new ConflictException("This class has already been decided.");
            }

            if (item.Payout.Status != PayoutStatus.Pending)
            {
                throw new DomainValidationException(
                    $"This class's payout is already {item.Payout.Status} and can no longer be changed.");
            }

            var fullAmount = item.AmountBeforeReview ?? item.Amount;
            var newAmount = request.Decision switch
            {
                PayoutReviewDecision.ApprovedFull => fullAmount,
                PayoutReviewDecision.Rejected => 0m,
                _ => request.Amount ?? ProRate(fullAmount, item.ScheduledMinutes, item.DeliveredMinutes),
            };
            if (request.Decision == PayoutReviewDecision.ApprovedPartial && (newAmount < 0 || newAmount > fullAmount))
            {
                throw new DomainValidationException($"A partial payout must be between 0 and the full amount ({fullAmount:0.00}).");
            }

            var label = request.Decision switch
            {
                PayoutReviewDecision.ApprovedFull => "Approved for full payout",
                PayoutReviewDecision.ApprovedPartial => $"Approved for partial payout ({newAmount:0.00} of {fullAmount:0.00})",
                _ => "Not approved for payout",
            };
            var decisionNote = string.IsNullOrWhiteSpace(request.Note) ? label : $"{label}: {request.Note.Trim()}";
            item.Note = string.IsNullOrEmpty(item.Note) ? decisionNote : $"{item.Note} ({decisionNote})";
            item.AmountBeforeReview = fullAmount;
            item.Payout.TotalAmount += newAmount - item.Amount;
            item.Amount = newAmount;
            item.RequiresReview = false;
            item.ReviewDecision = request.Decision;
            item.ReviewedByUserId = reviewerUserId;
            item.ReviewedAtUtc = DateTime.UtcNow;

            await _auditLog.StageAsync(AuditAction.Update, nameof(PayoutItem), item.Id.ToString(),
                changesJson: $"{{\"decision\":\"{request.Decision}\",\"amount\":{newAmount}}}", cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return (await ApprovalQueryAsync(i => i.Id == payoutItemId, cancellationToken)).First();
        }

        private static decimal ProRate(decimal fullAmount, int? scheduledMinutes, int? deliveredMinutes)
        {
            if (scheduledMinutes is not > 0)
            {
                return fullAmount;
            }

            var fraction = Math.Clamp((decimal)(deliveredMinutes ?? 0) / scheduledMinutes.Value, 0m, 1m);
            return Math.Round(fullAmount * fraction, 2);
        }

        private async Task<List<PayoutApprovalDto>> ApprovalQueryAsync(
            System.Linq.Expressions.Expression<Func<PayoutItem, bool>> filter,
            CancellationToken cancellationToken)
        {
            var items = await _unitOfWork.Repository<PayoutItem>().Query()
                .Where(i => i.Type == PayoutItemType.SessionEarning)
                .Where(filter)
                .Include(i => i.Payout).ThenInclude(p => p.TeacherProfile).ThenInclude(t => t.User)
                .Include(i => i.ClassSession).ThenInclude(s => s!.Batch)
                .OrderByDescending(i => i.CreatedAtUtc)
                .Take(500)
                .ToListAsync(cancellationToken);

            var sessionIds = items.Where(i => i.ClassSessionId != null).Select(i => i.ClassSessionId!.Value).Distinct().ToList();
            var batchIds = items.Where(i => i.ClassSession?.BatchId != null).Select(i => i.ClassSession!.BatchId!.Value).Distinct().ToList();
            var students = await _unitOfWork.Repository<BatchEnrollment>().Query()
                .Where(e => batchIds.Contains(e.BatchId) && e.Status == EnrollmentStatus.Active)
                .Select(e => new { e.BatchId, Name = e.Child.FirstName + " " + e.Child.LastName })
                .ToListAsync(cancellationToken);
            var demoChildren = await _unitOfWork.Repository<DemoBooking>().Query()
                .Where(b => b.ClassSessionId != null && sessionIds.Contains(b.ClassSessionId.Value))
                .Select(b => new { SessionId = b.ClassSessionId!.Value, b.ChildName })
                .ToListAsync(cancellationToken);
            var reviewerIds = items.Where(i => i.ReviewedByUserId != null).Select(i => i.ReviewedByUserId!.Value).Distinct().ToList();
            var reviewers = await _unitOfWork.Repository<User>().Query()
                .Where(u => reviewerIds.Contains(u.Id))
                .Select(u => new { u.Id, Name = u.FirstName + " " + u.LastName })
                .ToDictionaryAsync(u => u.Id, u => u.Name.Trim(), cancellationToken);

            return items.Select(i =>
            {
                var session = i.ClassSession;
                var fullAmount = i.AmountBeforeReview ?? i.Amount;
                string? studentNames = session?.BatchId is { } batchId
                    ? string.Join(", ", students.Where(s => s.BatchId == batchId).Select(s => s.Name.Trim()))
                    : demoChildren.FirstOrDefault(d => d.SessionId == session?.Id)?.ChildName;
                return new PayoutApprovalDto
                {
                    ItemId = i.Id,
                    PayoutId = i.PayoutId,
                    PayoutStatus = i.Payout.Status,
                    ClassSessionId = i.ClassSessionId,
                    TeacherName = $"{i.Payout.TeacherProfile.User.FirstName} {i.Payout.TeacherProfile.User.LastName}".Trim(),
                    ClassName = session?.Batch?.Name ?? (session?.Type == SessionType.Demo ? "Demo class" : "Class session"),
                    StudentNames = string.IsNullOrWhiteSpace(studentNames) ? null : studentNames,
                    ScheduledStartAtUtc = session?.ScheduledStartAtUtc,
                    ScheduledEndAtUtc = session?.ScheduledEndAtUtc,
                    ActualStartAtUtc = session?.ActualStartAtUtc,
                    ActualEndAtUtc = session?.ActualEndAtUtc,
                    ScheduledMinutes = i.ScheduledMinutes,
                    DeliveredMinutes = i.DeliveredMinutes,
                    ShortfallMinutes = i.ScheduledMinutes is { } sm ? sm - (i.DeliveredMinutes ?? 0) : null,
                    FullAmount = fullAmount,
                    ProRatedAmount = ProRate(fullAmount, i.ScheduledMinutes, i.DeliveredMinutes),
                    Amount = i.Amount,
                    Pending = i.RequiresReview,
                    Decision = i.ReviewDecision,
                    ReviewedByName = i.ReviewedByUserId is { } rid && reviewers.TryGetValue(rid, out var rn) ? rn : null,
                    ReviewedAtUtc = i.ReviewedAtUtc,
                    Note = i.Note,
                    CreatedAtUtc = i.CreatedAtUtc,
                };
            }).ToList();
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
            // Adjusting a flagged item from the payout statement is the same decision as
            // making it on Payout Approvals — record it so it shows there as decided.
            if (item.RequiresReview)
            {
                var fullAmount = item.AmountBeforeReview ?? item.Amount;
                item.AmountBeforeReview = fullAmount;
                item.ReviewDecision = request.NewAmount >= fullAmount ? PayoutReviewDecision.ApprovedFull
                    : request.NewAmount <= 0 ? PayoutReviewDecision.Rejected
                    : PayoutReviewDecision.ApprovedPartial;
                item.ReviewedAtUtc = DateTime.UtcNow;
            }
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
