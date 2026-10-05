using System.Security.Cryptography;
using iucs.readernest.application.Common;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Admission;
using iucs.readernest.application.Dto.Billing;
using iucs.readernest.application.Dto.Users;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Admission;
using iucs.readernest.domain.Entities.Billing;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace iucs.readernest.application.Services
{
    public interface IAdmissionPaymentService
    {
        Task<AdmissionPaymentLinkDto> SendPaymentLinkAsync(Guid demoBookingId, SendAdmissionPaymentLinkRequest request, CancellationToken cancellationToken = default);

        Task<PublicAdmissionPaymentDto> GetPublicPaymentAsync(string token, CancellationToken cancellationToken = default);

        Task<StartPublicAdmissionPaymentResultDto> StartPublicPaymentAsync(string token, StartPublicAdmissionPaymentRequest request, CancellationToken cancellationToken = default);

        Task<DemoBookingDto> VerifyAndEnrollAsync(Guid demoBookingId, CancellationToken cancellationToken = default);

        /// <summary>For a first-time parent who still has to accept the Terms, the portal /pay link that shows
        /// them before the gateway; null when no Terms are needed (the raw gateway link is fine then).</summary>
        Task<PaymentLinkDto?> TermsFirstPayLinkAsync(Guid invoiceId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The portal-only admission flow that replaces WhatsApp for the last leg of admission:
    /// Demo -> counsellor issues a course payment link at the agreed amount -> parent accepts the
    /// Terms and Conditions and pays -> the lead waits in PaymentReceived for the counsellor to
    /// verify and click Enroll -> the parent's login is emailed automatically (welcome email with
    /// portal link, email and PIN) and the lead moves to ReadyForEnrollment, after which the
    /// parent's own child-details form goes to the RM as before.
    ///
    /// The parent's account is created when the link is issued (an invoice needs a parent profile)
    /// but stays silent -- no email -- until Enroll, so a parent who never pays is never sent
    /// credentials for an admission that didn't happen.
    /// </summary>
    public class AdmissionPaymentService : IAdmissionPaymentService
    {
        /// <summary>Longer than a manual admission's week: the parent may still be deciding.</summary>
        private const int FeeDueDays = 30;

        private readonly IUnitOfWork _unitOfWork;
        private readonly IDemoBookingService _demoBookingService;
        private readonly IUserService _userService;
        private readonly IBillingService _billingService;
        private readonly INotificationService _notificationService;
        private readonly IAuditLogService _auditLog;
        private readonly IConfiguration _configuration;

        public AdmissionPaymentService(
            IUnitOfWork unitOfWork,
            IDemoBookingService demoBookingService,
            IUserService userService,
            IBillingService billingService,
            INotificationService notificationService,
            IAuditLogService auditLog,
            IConfiguration configuration)
        {
            _unitOfWork = unitOfWork;
            _demoBookingService = demoBookingService;
            _userService = userService;
            _billingService = billingService;
            _notificationService = notificationService;
            _auditLog = auditLog;
            _configuration = configuration;
        }

        public async Task<AdmissionPaymentLinkDto> SendPaymentLinkAsync(
            Guid demoBookingId,
            SendAdmissionPaymentLinkRequest request,
            CancellationToken cancellationToken = default)
        {
            // Same visibility as every other demo screen (a counsellor only sees their own leads).
            await _demoBookingService.GetAsync(demoBookingId, cancellationToken);

            var booking = await _unitOfWork.Repository<DemoBooking>().TrackedQuery()
                .Include(b => b.Invoice)
                .FirstOrDefaultAsync(b => b.Id == demoBookingId, cancellationToken)
                ?? throw new NotFoundException(nameof(DemoBooking), demoBookingId);

            if (booking.ConversionStatus is ConversionStatus.Enrolled or ConversionStatus.NotInterested
                or ConversionStatus.ReadyForEnrollment or ConversionStatus.PaymentReceived)
            {
                throw new DomainValidationException(
                    $"This lead is already {booking.ConversionStatus}; a new payment link can't be issued.");
            }

            // A manual admission already invoiced this lead through its own path (the child exists
            // and a fee invoice is outstanding); a second invoice here would bill the family twice.
            if (booking.InvoiceId.HasValue && booking.PaymentToken is null)
            {
                throw new DomainValidationException("This lead was admitted manually and already has an invoice -- use Payments to share that link.");
            }

            var course = await _unitOfWork.Repository<Course>().Query()
                .Include(c => c.Department)
                .FirstOrDefaultAsync(c => c.Id == request.CourseId, cancellationToken)
                ?? throw new NotFoundException(nameof(Course), request.CourseId);

            var amount = request.Amount ?? course.Price;
            if (amount <= 0)
            {
                throw new DomainValidationException("The course has no fee set -- enter the agreed amount.");
            }
            // No ceiling at the list price: the counsellor sometimes has to collect more than it
            // (e.g. classes outside the 10 AM-10 PM IST window are priced differently).
            if (!await _unitOfWork.Repository<PaymentAccount>().ExistsAsync(
                    a => a.DepartmentId == course.DepartmentId && a.IsActive, cancellationToken))
            {
                throw new DomainValidationException(
                    $"No active payment account is set up for the {course.Department.Name} department. Add one in Billing -> Payment accounts.");
            }

            // Re-issuing (a corrected amount or another course): the earlier invoice is voided,
            // but only while nothing has been paid on it -- money already received is never
            // silently orphaned.
            if (booking.Invoice is { } previous)
            {
                if (previous.AmountPaid > 0)
                {
                    throw new DomainValidationException(
                        "The parent has already paid part of the earlier link, so it can't be replaced. Record or refund that payment in Billing first.");
                }
                if (previous.Status != InvoiceStatus.Cancelled)
                {
                    previous.Status = InvoiceStatus.Cancelled;
                    _unitOfWork.Repository<Invoice>().Update(previous);
                    await _auditLog.StageAsync(AuditAction.Update, nameof(Invoice), previous.Id.ToString(),
                        "{\"note\":\"Replaced by a new admission payment link\"}", cancellationToken);
                }
            }

            var parentProfile = await EnsureParentProfileAsync(booking, cancellationToken);

            var invoice = await _billingService.CreateInvoiceAsync(
                new CreateInvoiceRequest
                {
                    ParentProfileId = parentProfile.Id,
                    CourseId = course.Id,
                    DepartmentId = course.DepartmentId,
                    Amount = amount,
                    DueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(FeeDueDays),
                },
                cancellationToken,
                notifyParent: false);

            var tracked = await _unitOfWork.Repository<DemoBooking>().TrackedQuery()
                .FirstAsync(b => b.Id == demoBookingId, cancellationToken);
            tracked.PaymentToken ??= NewToken();
            tracked.CourseId = course.Id;
            tracked.InvoiceId = invoice.Id;
            tracked.TermsAcceptedAtUtc = null;
            tracked.PaymentLinkUrl = $"{BaseUrl()}/pay/{tracked.PaymentToken}";
            tracked.ConversionStatus = ConversionStatus.PaymentPending;
            await _auditLog.StageAsync(AuditAction.Update, nameof(DemoBooking), tracked.Id.ToString(),
                $"{{\"note\":\"Admission payment link issued\",\"amount\":{amount}}}", cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var shareMessage = string.Join("\n",
                $"Hello {tracked.ParentName},",
                "",
                tracked.ClassSessionId is null
                    ? $"Thank you for choosing The Reader Nest. To confirm {tracked.ChildName}'s admission for {course.Name}, please complete the payment of {invoice.Currency} {amount:0.##} here:"
                    : $"Thank you for attending the demo class. To confirm {tracked.ChildName}'s admission for {course.Name}, please complete the payment of {invoice.Currency} {amount:0.##} here:",
                tracked.PaymentLinkUrl,
                "",
                "You'll be asked to accept our Terms & Conditions before paying. Once we verify your payment, your login details will be emailed to you automatically.");

            return new AdmissionPaymentLinkDto
            {
                Booking = await _demoBookingService.GetAsync(demoBookingId, cancellationToken),
                PaymentUrl = tracked.PaymentLinkUrl,
                CourseName = course.Name,
                ListPrice = course.Price,
                Amount = amount,
                Currency = invoice.Currency,
                ShareMessage = shareMessage,
            };
        }

        public async Task<PublicAdmissionPaymentDto> GetPublicPaymentAsync(string token, CancellationToken cancellationToken = default)
        {
            var link = await LoadPayLinkAsync(token, cancellationToken);
            var invoice = link.Invoice;
            return new PublicAdmissionPaymentDto
            {
                ParentName = link.ParentName,
                ChildName = link.ChildName,
                CourseName = link.CourseName,
                Amount = invoice.Amount,
                AmountPaid = invoice.AmountPaid,
                Currency = invoice.Currency,
                IsPaid = invoice.Status == InvoiceStatus.Paid,
                TermsRequired = await TermsRule.IsAcceptanceRequiredAsync(_unitOfWork, link.Parent, cancellationToken),
                TermsUrl = await PolicySettings.GetAsync(_unitOfWork, PolicySettings.TermsUrlKey, cancellationToken),
                TermsText = await PolicySettings.GetAsync(_unitOfWork, PolicySettings.TermsTextKey, cancellationToken),
            };
        }

        public async Task<StartPublicAdmissionPaymentResultDto> StartPublicPaymentAsync(
            string token,
            StartPublicAdmissionPaymentRequest request,
            CancellationToken cancellationToken = default)
        {
            var link = await LoadPayLinkAsync(token, cancellationToken);
            var invoice = link.Invoice;
            if (invoice.Status == InvoiceStatus.Paid)
            {
                throw new DomainValidationException("This payment has already been completed. Thank you!");
            }

            // Terms are only asked of a first-time parent, at their FIRST payment (TermsRule); after
            // that (this or a later admission, or Pay Now) they are never asked again.
            if (await TermsRule.IsAcceptanceRequiredAsync(_unitOfWork, link.Parent, cancellationToken))
            {
                if (!request.TermsAccepted)
                {
                    throw new DomainValidationException("Please accept the Terms & Conditions to continue with the payment.");
                }

                var acceptedAt = DateTime.UtcNow;
                if (link.Booking is { } booking)
                {
                    var trackedBooking = await _unitOfWork.Repository<DemoBooking>().TrackedQuery()
                        .FirstAsync(b => b.Id == booking.Id, cancellationToken);
                    trackedBooking.TermsAcceptedAtUtc = acceptedAt;
                }
                if (link.Parent is { } parent)
                {
                    var trackedParent = await _unitOfWork.Repository<ParentProfile>().TrackedQuery()
                        .FirstAsync(p => p.Id == parent.Id, cancellationToken);
                    trackedParent.TermsAcceptedAtUtc ??= acceptedAt;
                }

                await _auditLog.StageAsync(AuditAction.Update, nameof(Invoice), invoice.Id.ToString(),
                    "{\"note\":\"Parent accepted the Terms & Conditions at their first payment\"}", cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            var gatewayLink = await _billingService.CreatePaymentLinkAsync(invoice.Id, cancellationToken);
            return new StartPublicAdmissionPaymentResultDto { Url = gatewayLink.Url };
        }

        public async Task<PaymentLinkDto?> TermsFirstPayLinkAsync(Guid invoiceId, CancellationToken cancellationToken = default)
        {
            var invoice = await _unitOfWork.Repository<Invoice>().Query()
                .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
            if (invoice is null || invoice.Status is InvoiceStatus.Paid or InvoiceStatus.Cancelled)
            {
                return null;
            }

            var parent = await _unitOfWork.Repository<ParentProfile>().Query()
                .FirstOrDefaultAsync(p => p.Id == invoice.ParentProfileId, cancellationToken);
            if (!await TermsRule.IsAcceptanceRequiredAsync(_unitOfWork, parent, cancellationToken))
            {
                return null;
            }

            return new PaymentLinkDto
            {
                InvoiceId = invoice.Id,
                InvoiceNumber = invoice.InvoiceNumber,
                Url = $"{BaseUrl()}/pay/{InvoicePayToken.Create(invoiceId, _configuration["Jwt:SigningKey"])}",
                GatewayReference = string.Empty,
                AmountDue = invoice.Amount - invoice.AmountPaid,
            };
        }

        public async Task<DemoBookingDto> VerifyAndEnrollAsync(Guid demoBookingId, CancellationToken cancellationToken = default)
        {
            await _demoBookingService.GetAsync(demoBookingId, cancellationToken);

            var booking = await _unitOfWork.Repository<DemoBooking>().TrackedQuery()
                .Include(b => b.Invoice)
                .FirstOrDefaultAsync(b => b.Id == demoBookingId, cancellationToken)
                ?? throw new NotFoundException(nameof(DemoBooking), demoBookingId);

            if (booking.PaymentToken is null || booking.Invoice is null)
            {
                throw new DomainValidationException("No admission payment link was issued for this lead, so there is no payment to verify.");
            }
            if (booking.ConversionStatus is ConversionStatus.ReadyForEnrollment or ConversionStatus.Enrolled)
            {
                throw new DomainValidationException("This lead is already enrolled.");
            }
            // No separate verification step and no need for the full amount (client decision
            // 2026-09-26): the counsellor can Enroll as soon as ANY payment has been received.
            // The balance of a part payment simply stays due on the invoice.
            if (booking.Invoice.AmountPaid <= 0)
            {
                throw new DomainValidationException(
                    "No payment has been received for this lead yet. Enroll as soon as the parent's payment comes in.");
            }

            var email = booking.ParentEmail.Trim().ToLowerInvariant();
            var user = await _unitOfWork.Repository<User>().Query()
                .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
            if (user is null)
            {
                await EnsureParentProfileAsync(booking, cancellationToken);
                user = await _unitOfWork.Repository<User>().Query().FirstAsync(u => u.Email == email, cancellationToken);
            }
            if (user.Role != UserRole.Parent)
            {
                throw new DomainValidationException("That email belongs to a staff account, not a parent.");
            }

            // The PIN the silent account was created with. A sibling's earlier account may be past
            // that (they chose their own PIN) -- the email then points them at their existing login.
            string pinText;
            try
            {
                pinText = await _userService.RevealPinAsync(user.Id, cancellationToken);
            }
            catch (DomainValidationException)
            {
                pinText = "Your existing PIN (you already have an account with us)";
            }

            // A parent without an email logs in with their mobile number: there is no address to send
            // the welcome email to, so the counsellor is handed the PIN once, to send on WhatsApp as
            // they do today. Everyone else gets the welcome email automatically.
            var phoneOnly = ParentLogin.IsPlaceholderEmail(user.Email);
            if (!phoneOnly)
            {
                // Sent BEFORE the status moves, so a failed send leaves the lead un-enrolled for the
                // counsellor to retry rather than "enrolled" with a parent who never got a login.
                await _notificationService.SendTemplatedEmailAsync(
                    user.Id,
                    user.Email,
                    NotificationType.General,
                    "parent-enrollment-welcome",
                    new Dictionary<string, string>
                    {
                        ["FirstName"] = user.FirstName,
                        ["Email"] = user.Email,
                        ["TemporaryPin"] = pinText,
                        ["PortalUrl"] = BaseUrl(),
                        ["LoginUrl"] = BaseUrl() + "/login",
                    },
                    cancellationToken);
            }

            var tracked = await _unitOfWork.Repository<DemoBooking>().TrackedQuery()
                .FirstAsync(b => b.Id == demoBookingId, cancellationToken);
            tracked.ConversionStatus = ConversionStatus.ReadyForEnrollment;
            tracked.PaymentVerifiedAtUtc = DateTime.UtcNow;
            await _auditLog.StageAsync(AuditAction.Update, nameof(DemoBooking), tracked.Id.ToString(),
                phoneOnly
                    ? "{\"note\":\"Enrolled by counsellor after payment; phone-only parent, PIN handed to staff for WhatsApp\"}"
                    : "{\"note\":\"Enrolled by counsellor after payment; welcome email sent\"}",
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var result = await _demoBookingService.GetAsync(demoBookingId, cancellationToken);
            if (phoneOnly)
            {
                var loginId = booking.ParentPhone ?? string.Empty;
                var loginUrl = BaseUrl() + "/login";
                result.IssuedLogin = new IssuedParentLoginDto
                {
                    LoginId = loginId,
                    TemporaryPin = pinText,
                    LoginUrl = loginUrl,
                    WhatsAppMessage = string.Join("\n",
                        $"Hello {booking.ParentName},",
                        "",
                        "Your The Reader Nest parent portal login:",
                        loginUrl,
                        $"Login: {loginId}",
                        $"PIN: {pinText}",
                        "",
                        "After logging in, please fill in your child's details.",
                        "",
                        "Thank you!"),
                };
            }

            return result;
        }


        /// <summary>The parent's account (silent, no email) and profile, reused if one exists.</summary>
        private async Task<ParentProfile> EnsureParentProfileAsync(DemoBooking booking, CancellationToken cancellationToken)
        {
            var email = booking.ParentEmail.Trim().ToLowerInvariant();
            var existing = await _unitOfWork.Repository<User>().Query()
                .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
            if (existing is null)
            {
                var nameParts = booking.ParentName.Trim().Split(' ', 2);
                var created = await _userService.CreateAsync(
                    new CreateUserRequest
                    {
                        Email = email,
                        FirstName = nameParts[0],
                        LastName = nameParts.Length > 1 ? nameParts[1] : string.Empty,
                        Phone = booking.ParentPhone,
                        Role = UserRole.Parent,
                        SuppressWelcomeEmail = true,
                    },
                    cancellationToken);
                existing = await _unitOfWork.Repository<User>().Query().FirstAsync(u => u.Id == created.Id, cancellationToken);
            }
            else if (existing.Role != UserRole.Parent)
            {
                throw new DomainValidationException("That email belongs to a staff account, not a parent.");
            }

            return await _unitOfWork.Repository<ParentProfile>().Query()
                .FirstOrDefaultAsync(p => p.UserId == existing.Id, cancellationToken)
                ?? throw new DomainValidationException("This parent account has no parent profile.");
        }

        /// <summary>Everything the public payment page needs, from either kind of /pay/{code} link.</summary>
        private sealed record PayLink(Invoice Invoice, DemoBooking? Booking, ParentProfile? Parent, string ParentName, string ChildName, string CourseName);

        private async Task<PayLink> LoadPayLinkAsync(string token, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(token) && InvoicePayToken.LooksLikeInvoiceToken(token))
            {
                // A staff-shared invoice link (Payments, manual admission) for a first-time parent.
                var invoiceId = InvoicePayToken.Read(token, _configuration["Jwt:SigningKey"])
                    ?? throw new NotFoundException("This payment link is not valid.");
                var invoice = await _unitOfWork.Repository<Invoice>().Query()
                    .Include(i => i.Course)
                    .Include(i => i.Child)
                    .Include(i => i.ParentProfile).ThenInclude(p => p.User)
                    .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
                if (invoice is null || invoice.Status == InvoiceStatus.Cancelled)
                {
                    throw new NotFoundException("This payment link is no longer valid. Please ask your counsellor for a new one.");
                }

                var demo = await _unitOfWork.Repository<DemoBooking>().Query()
                    .FirstOrDefaultAsync(b => b.InvoiceId == invoice.Id, cancellationToken);
                return new PayLink(
                    invoice,
                    demo,
                    invoice.ParentProfile,
                    $"{invoice.ParentProfile.User.FirstName} {invoice.ParentProfile.User.LastName}".Trim(),
                    invoice.Child is { } child ? $"{child.FirstName} {child.LastName}".Trim()
                        : string.IsNullOrWhiteSpace(demo?.ChildName) ? "your child" : demo.ChildName,
                    invoice.Course?.Name ?? "Course fee");
            }

            var booking = await LoadByTokenAsync(token, cancellationToken);
            var email = booking.ParentEmail.Trim().ToLowerInvariant();
            var bookingParent = await _unitOfWork.Repository<ParentProfile>().Query()
                .FirstOrDefaultAsync(p => p.User.Email == email, cancellationToken);
            return new PayLink(booking.Invoice!, booking, bookingParent, booking.ParentName, booking.ChildName, booking.Course?.Name ?? "Course");
        }

        private async Task<DemoBooking> LoadByTokenAsync(string token, CancellationToken cancellationToken)
        {
            // Not found for every bad shape alike, so the endpoint can't be used to probe tokens.
            if (string.IsNullOrWhiteSpace(token) || token.Length > 64)
            {
                throw new NotFoundException("This payment link is not valid.");
            }

            var booking = await _unitOfWork.Repository<DemoBooking>().Query()
                .Include(b => b.Invoice)
                .Include(b => b.Course)
                .FirstOrDefaultAsync(b => b.PaymentToken == token, cancellationToken);
            if (booking?.Invoice is null || booking.Invoice.Status == InvoiceStatus.Cancelled)
            {
                throw new NotFoundException("This payment link is no longer valid. Please ask your counsellor for a new one.");
            }

            return booking;
        }

        private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        private string BaseUrl() => (_configuration["Frontend:BaseUrl"] ?? "https://thereadernest.in").TrimEnd('/');
    }
}
