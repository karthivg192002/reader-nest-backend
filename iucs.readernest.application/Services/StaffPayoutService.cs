using iucs.readernest.application.Common;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Payouts;
using iucs.readernest.application.Mappings;
using iucs.readernest.domain.Entities.Admission;
using iucs.readernest.domain.Entities.Billing;
using iucs.readernest.domain.Entities.Payouts;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class StaffPayoutService : IStaffPayoutService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;

        public StaffPayoutService(IUnitOfWork unitOfWork, IAuditLogService auditLog)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
        }

        private static readonly UserRole[] EligibleRoles = [UserRole.SubAdmin, UserRole.AdmissionTeam, UserRole.Admin];

        public async Task<IReadOnlyList<EligibleStaffDto>> ListEligibleStaffAsync(CancellationToken cancellationToken = default)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var users = await _unitOfWork.Repository<User>().Query()
                .Include(u => u.RoleDefinition)
                .Where(u => u.Status == UserStatus.Active && EligibleRoles.Contains(u.Role))
                .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
                .ToListAsync(cancellationToken);

            var userIds = users.Select(u => u.Id).ToList();
            var settings = await _unitOfWork.Repository<StaffCompensationSetting>().Query()
                .Where(s => userIds.Contains(s.UserId) && s.EffectiveFrom <= today)
                .ToListAsync(cancellationToken);
            var currentByUser = settings
                .GroupBy(s => s.UserId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.EffectiveFrom).First());

            return users.Select(u => new EligibleStaffDto
            {
                UserId = u.Id,
                Name = $"{u.FirstName} {u.LastName}".Trim(),
                Email = u.Email,
                Role = u.Role,
                RoleName = u.RoleDefinition?.DisplayName,
                Compensation = currentByUser.TryGetValue(u.Id, out var setting) ? setting.ToDto() : null,
            }).ToList();
        }

        public async Task<StaffCompensationSettingDto> SetCompensationAsync(
            Guid userId, SaveStaffCompensationRequest request, CancellationToken cancellationToken = default)
        {
            var user = await _unitOfWork.Repository<User>().Query().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                ?? throw new NotFoundException("User not found.");
            if (!EligibleRoles.Contains(user.Role))
            {
                throw new DomainValidationException(
                    "Compensation can only be set for Coordinator / Relationship Manager / Admission / Admin staff — teachers have their own batch-wise payout, and parents/students don't apply.");
            }

            var effectiveFrom = request.EffectiveFrom ?? DateOnly.FromDateTime(DateTime.UtcNow);

            // Same-day edit updates in place; a later effective date appends a row so a month
            // that's already been frozen (or hasn't happened yet) stays computed from whatever
            // was actually in force during it.
            var setting = await _unitOfWork.Repository<StaffCompensationSetting>().FirstOrDefaultAsync(
                s => s.UserId == userId && s.EffectiveFrom == effectiveFrom, cancellationToken);
            if (setting is null)
            {
                setting = new StaffCompensationSetting { UserId = userId, EffectiveFrom = effectiveFrom };
                await _unitOfWork.Repository<StaffCompensationSetting>().AddAsync(setting, cancellationToken);
            }

            setting.Basis = request.Basis;
            setting.FixedMonthlyAmount = request.Basis == CompensationBasis.FixedMonthly ? request.FixedMonthlyAmount : null;
            setting.CollectionPercentage = request.Basis == CompensationBasis.PercentOfMonthlyCollection ? request.CollectionPercentage : null;

            await _auditLog.StageAsync(AuditAction.Update, nameof(StaffCompensationSetting), setting.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return setting.ToDto();
        }

        public async Task<StaffEarningsDto> GetEarningsAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var user = await _unitOfWork.Repository<User>().Query().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                ?? throw new NotFoundException("User not found.");

            var now = DateTime.UtcNow;
            var current = await ComputePeriodAsync(user, now.Year, now.Month, freeze: false, cancellationToken);

            // Fresh salary calculation starts on the 1st of every month (client requirement); the
            // earliest month worth showing is whichever month compensation was first configured
            // for this person — nothing to compute or freeze before that.
            var earliestEffective = await _unitOfWork.Repository<StaffCompensationSetting>().Query()
                .Where(s => s.UserId == userId)
                .OrderBy(s => s.EffectiveFrom)
                .Select(s => (DateOnly?)s.EffectiveFrom)
                .FirstOrDefaultAsync(cancellationToken);

            var history = new List<StaffPayoutDto>();
            if (earliestEffective is { } start)
            {
                var cursor = new DateTime(start.Year, start.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                var currentMonthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                while (cursor < currentMonthStart)
                {
                    history.Add(await ComputePeriodAsync(user, cursor.Year, cursor.Month, freeze: true, cancellationToken));
                    cursor = cursor.AddMonths(1);
                }

                history.Reverse(); // newest first
            }

            return new StaffEarningsDto { CurrentMonth = current, History = history };
        }

        public async Task<StaffPayoutDto> MarkPaidAsync(Guid staffPayoutId, CancellationToken cancellationToken = default)
        {
            var payout = await _unitOfWork.Repository<StaffPayout>().FirstOrDefaultAsync(p => p.Id == staffPayoutId, cancellationToken)
                ?? throw new NotFoundException(nameof(StaffPayout), staffPayoutId);

            if (payout.Status != StaffPayoutStatus.Finalized)
            {
                throw new DomainValidationException($"This month's payout is already {payout.Status} and can't be marked paid again.");
            }

            payout.Status = StaffPayoutStatus.Paid;
            payout.PaidAtUtc = DateTime.UtcNow;

            await _auditLog.StageAsync(AuditAction.Update, nameof(StaffPayout), payout.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var name = await _unitOfWork.Repository<User>().Query()
                .Where(u => u.Id == payout.UserId)
                .Select(u => u.FirstName + " " + u.LastName)
                .FirstOrDefaultAsync(cancellationToken);
            return payout.ToDto(name?.Trim());
        }

        /// <summary>
        /// The current month is always live (recomputed, never saved — freeze=false). A past
        /// month is returned from its frozen StaffPayout row if one already exists; otherwise
        /// (the first time anyone looks at it) it's computed once from that month's own
        /// effective setting and collections, then saved so it never changes again.
        /// </summary>
        private async Task<StaffPayoutDto> ComputePeriodAsync(
            User user, int year, int month, bool freeze, CancellationToken cancellationToken)
        {
            var staffName = $"{user.FirstName} {user.LastName}".Trim();

            if (freeze)
            {
                var existing = await _unitOfWork.Repository<StaffPayout>().Query()
                    .FirstOrDefaultAsync(p => p.UserId == user.Id && p.PeriodYear == year && p.PeriodMonth == month, cancellationToken);
                if (existing is not null)
                {
                    return existing.ToDto(staffName);
                }
            }

            // Whatever was in force by the end of that month — an edit made any time during the
            // month applies to the whole month; once frozen above, no later edit reaches it at all.
            var asOf = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
            var setting = await _unitOfWork.Repository<StaffCompensationSetting>().Query()
                .Where(s => s.UserId == user.Id && s.EffectiveFrom <= asOf)
                .OrderByDescending(s => s.EffectiveFrom)
                .FirstOrDefaultAsync(cancellationToken);

            var basis = setting?.Basis ?? CompensationBasis.FixedMonthly;
            decimal amount;
            decimal? collectionRaw = null, collectionRounded = null, appliedPercentage = null, appliedFixed = null;

            if (basis == CompensationBasis.FixedMonthly)
            {
                amount = setting?.FixedMonthlyAmount ?? 0m;
                appliedFixed = amount;
            }
            else
            {
                var monthStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
                var monthEnd = monthStart.AddMonths(1);

                // "Total amount collected by them during the month" (client requirement): every
                // successful payment that is THIS staff member's collection -- their own demo
                // leads' payments (even when the parent paid online), otherwise invoices they
                // created. Same rule as the counselor's own dashboard figure (CollectionOwnership).
                collectionRaw = await CollectionOwnership.OwnedBy(
                        _unitOfWork.Repository<PaymentTransaction>().Query(),
                        _unitOfWork.Repository<DemoBooking>().Query(),
                        user.Id)
                    .Where(t => t.Status == TransactionStatus.Success
                        && t.PaidAtUtc != null && t.PaidAtUtc >= monthStart && t.PaidAtUtc < monthEnd)
                    .SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0m;

                // Floored DOWN to the nearest ₹10,000 before the percentage is applied (client
                // requirement) — ₹4,64,000 and ₹4,69,000 both count as ₹4,60,000; only reaching
                // ₹4,70,000 moves the eligible amount up.
                collectionRounded = Math.Floor(collectionRaw.Value / 10000m) * 10000m;
                appliedPercentage = setting?.CollectionPercentage ?? 0m;
                amount = Math.Round(collectionRounded.Value * appliedPercentage.Value / 100m, 2);
            }

            if (!freeze)
            {
                return new StaffPayoutDto
                {
                    Id = null,
                    UserId = user.Id,
                    StaffName = staffName,
                    PeriodYear = year,
                    PeriodMonth = month,
                    Basis = basis,
                    Amount = amount,
                    CollectionAmountRaw = collectionRaw,
                    CollectionAmountRounded = collectionRounded,
                    AppliedPercentage = appliedPercentage,
                    AppliedFixedAmount = appliedFixed,
                    IsFinalized = false,
                    Status = null,
                    PaidAtUtc = null,
                };
            }

            var frozen = new StaffPayout
            {
                UserId = user.Id,
                PeriodYear = year,
                PeriodMonth = month,
                Basis = basis,
                Amount = amount,
                CollectionAmountRaw = collectionRaw,
                CollectionAmountRounded = collectionRounded,
                AppliedPercentage = appliedPercentage,
                AppliedFixedAmount = appliedFixed,
                Status = StaffPayoutStatus.Finalized,
            };
            await _unitOfWork.Repository<StaffPayout>().AddAsync(frozen, cancellationToken);
            await _auditLog.StageAsync(AuditAction.Create, nameof(StaffPayout), frozen.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return frozen.ToDto(staffName);
        }
    }
}
