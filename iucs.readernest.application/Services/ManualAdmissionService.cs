using System.Text.Json;
using iucs.readernest.application.Common;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Dto.Admission;
using iucs.readernest.application.Dto.Billing;
using iucs.readernest.application.Dto.Enrollment;
using iucs.readernest.application.Dto.Users;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Admission;
using iucs.readernest.domain.Entities.Billing;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace iucs.readernest.application.Services
{
    public interface IManualAdmissionService
    {
        Task<ManualAdmissionResultDto> AdmitAsync(Guid demoBookingId, ManualAdmissionRequest request, CancellationToken cancellationToken = default);

        Task<ManualAdmissionOptionsDto> GetOptionsAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// "Admit manually" after a demo, for parents who share nothing by email and want it all on
    /// WhatsApp. The counselor enters what the parent's own enrollment form would have held and
    /// this runs the normal pipeline on their behalf — parent account (mobile-number login when
    /// there's no email), an enrollment form approved through EnrollmentService.ReviewAsync (so
    /// child, subscription/first invoice and batch seat happen exactly as a regular approval),
    /// then a payment link. The demo moves to PaymentPending and flips to Enrolled once the
    /// invoice is fully paid (BillingService.ApplyPaymentToInvoiceAsync).
    /// Safe to retry after a partial failure: an existing account and a still-unreviewed form
    /// for this demo are reused rather than duplicated.
    /// </summary>
    public class ManualAdmissionService : IManualAdmissionService
    {
        /// <summary>A manually admitted student's course-fee invoice is due a week out.</summary>
        private const int FeeDueDays = 7;

        private readonly IUnitOfWork _unitOfWork;
        private readonly IDemoBookingService _demoBookingService;
        private readonly IUserService _userService;
        private readonly IEnrollmentService _enrollmentService;
        private readonly IBillingService _billingService;
        private readonly IBatchService _batchService;
        private readonly IAuditLogService _auditLog;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ManualAdmissionService> _logger;

        public ManualAdmissionService(
            IUnitOfWork unitOfWork,
            IDemoBookingService demoBookingService,
            IUserService userService,
            IEnrollmentService enrollmentService,
            IBillingService billingService,
            IBatchService batchService,
            IAuditLogService auditLog,
            IConfiguration configuration,
            ILogger<ManualAdmissionService> logger)
        {
            _unitOfWork = unitOfWork;
            _demoBookingService = demoBookingService;
            _userService = userService;
            _enrollmentService = enrollmentService;
            _billingService = billingService;
            _batchService = batchService;
            _auditLog = auditLog;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<ManualAdmissionResultDto> AdmitAsync(
            Guid demoBookingId,
            ManualAdmissionRequest request,
            CancellationToken cancellationToken = default)
        {
            // Same visibility as the rest of the demo screens (a counselor/RM only sees their own).
            await _demoBookingService.GetAsync(demoBookingId, cancellationToken);

            var booking = await _unitOfWork.Repository<DemoBooking>()
                .FirstOrDefaultAsync(b => b.Id == demoBookingId, cancellationToken)
                ?? throw new NotFoundException(nameof(DemoBooking), demoBookingId);

            // A staff admission that died half-way (child created and demo flipped to Enrolled, but
            // the plan/batch/invoice step failed) must be resumable, not dead-ended by the
            // "already admitted" check. Only a demo that got its invoice, or was enrolled some
            // other way, is truly done.
            var priorForm = await _unitOfWork.Repository<EnrollmentForm>()
                .FirstOrDefaultAsync(f => f.DemoBookingId == booking.Id
                    && f.Status == EnrollmentFormStatus.Approved
                    && f.ChildId != null
                    && f.FormDataJson.Contains("enteredByStaff"), cancellationToken);
            if (booking.InvoiceId.HasValue || (booking.ConversionStatus == ConversionStatus.Enrolled && priorForm is null))
            {
                throw new DomainValidationException("This demo has already been admitted.");
            }

            var loginEmail = ParentLogin.ResolveLoginEmail(request.ParentEmail, request.ParentPhone);
            var phone = string.IsNullOrWhiteSpace(request.ParentPhone) ? null : request.ParentPhone.Trim();
            if (request.ChildDateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow))
            {
                throw new DomainValidationException("Child's date of birth cannot be in the future.");
            }

            // What to bill: a package plan (recurring subscription) where the org uses them, or —
            // the usual case — a course's one-off fee (course price, editable). A batch alone
            // implies its course. Everything is checked here, before anything is created.
            PackagePlan? plan = null;
            Course? course = null;
            decimal feeAmount = 0;
            if (request.PackagePlanId.HasValue)
            {
                plan = await _unitOfWork.Repository<PackagePlan>().Query()
                    .FirstOrDefaultAsync(p => p.Id == request.PackagePlanId.Value, cancellationToken)
                    ?? throw new NotFoundException(nameof(PackagePlan), request.PackagePlanId.Value);
                if (!plan.IsActive)
                {
                    throw new DomainValidationException($"Course plan '{plan.Name}' is inactive — pick an active plan.");
                }
            }
            else
            {
                var courseId = request.CourseId;
                if (!courseId.HasValue && request.BatchId.HasValue)
                {
                    courseId = await _unitOfWork.Repository<Batch>().Query()
                        .Where(b => b.Id == request.BatchId.Value)
                        .Select(b => (Guid?)b.CourseId)
                        .FirstOrDefaultAsync(cancellationToken);
                }
                if (!courseId.HasValue)
                {
                    throw new DomainValidationException("Pick the course the student is joining.");
                }

                course = await _unitOfWork.Repository<Course>().Query()
                    .Include(c => c.Department)
                    .FirstOrDefaultAsync(c => c.Id == courseId.Value, cancellationToken)
                    ?? throw new NotFoundException(nameof(Course), courseId.Value);
                feeAmount = request.Amount ?? course.Price;
                if (feeAmount < 0)
                {
                    throw new DomainValidationException("The fee can't be negative.");
                }
                var hasAccount = await _unitOfWork.Repository<PaymentAccount>().ExistsAsync(
                    a => a.DepartmentId == course.DepartmentId && a.IsActive, cancellationToken);
                if (feeAmount > 0 && !hasAccount)
                {
                    throw new DomainValidationException(
                        $"No active payment account is set up for the {course.Department.Name} department, so no invoice can be made. Add one in Billing → Payment accounts.");
                }
            }

            // Keep the demo's own contact details in step with what was just confirmed.
            booking.ParentName = request.ParentName.Trim();
            booking.ParentEmail = loginEmail;
            booking.ParentPhone = phone;
            booking.ConversionStatus = ConversionStatus.DemoCompleted;
            _unitOfWork.Repository<DemoBooking>().Update(booking);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Parent account: reuse by login key; a phone-only parent may also already exist
            // under a real email with this same mobile number (e.g. a sibling's admission).
            var parent = await _unitOfWork.Repository<User>().Query()
                .FirstOrDefaultAsync(u => u.Email == loginEmail, cancellationToken);
            if (parent is null && ParentLogin.IsPlaceholderEmail(loginEmail))
            {
                var normalizedPhone = ParentLogin.NormalizePhone(phone)!;
                var lastFour = normalizedPhone[^4..];
                var samePhone = (await _unitOfWork.Repository<User>().Query()
                        .Where(u => u.Role == UserRole.Parent && u.Phone != null && u.Phone.Contains(lastFour))
                        .ToListAsync(cancellationToken))
                    .Where(u => ParentLogin.NormalizePhone(u.Phone) == normalizedPhone)
                    .ToList();
                if (samePhone.Count == 1)
                {
                    parent = samePhone[0];
                }
            }

            string? temporaryPin = null;
            if (parent is null)
            {
                var nameParts = request.ParentName.Trim().Split(' ', 2);
                var created = await _userService.CreateAsync(
                    new CreateUserRequest
                    {
                        Email = loginEmail,
                        FirstName = nameParts[0],
                        LastName = nameParts.Length > 1 ? nameParts[1] : string.Empty,
                        Phone = phone,
                        Role = UserRole.Parent,
                    },
                    cancellationToken);
                parent = await _unitOfWork.Repository<User>().Query()
                    .FirstAsync(u => u.Id == created.Id, cancellationToken);
                // Only for an account made just now: this is the counselor's one chance to hand
                // the parent their PIN on WhatsApp (no email will carry it).
                temporaryPin = await _userService.RevealPinAsync(parent.Id, cancellationToken);
            }
            else if (parent.Role != UserRole.Parent)
            {
                throw new DomainValidationException("That email/mobile number belongs to a staff account, not a parent.");
            }
            else if (booking.ParentEmail != parent.Email)
            {
                // Found by mobile number under their real email: the demo must carry the
                // account's own key or the parent portal won't link it to them.
                booking.ParentEmail = parent.Email;
                _unitOfWork.Repository<DemoBooking>().Update(booking);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            var parentProfile = await _unitOfWork.Repository<ParentProfile>().Query()
                .FirstOrDefaultAsync(p => p.UserId == parent.Id, cancellationToken)
                ?? throw new DomainValidationException("This parent account has no parent profile.");

            Guid childId;
            if (priorForm is not null)
            {
                // Resume: the child already exists from the earlier attempt; finish only what's missing.
                childId = priorForm.ChildId!.Value;
                if (plan is not null && !await _unitOfWork.Repository<Subscription>().ExistsAsync(
                        s => s.ChildId == childId && s.PackagePlanId == plan.Id && s.Status == SubscriptionStatus.Active, cancellationToken))
                {
                    await _billingService.CreateSubscriptionAsync(
                        new CreateSubscriptionRequest
                        {
                            ParentProfileId = parentProfile.Id,
                            ChildId = childId,
                            PackagePlanId = plan.Id,
                            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
                            PriceOverride = request.Amount,
                        },
                        cancellationToken);
                }
                if (request.BatchId.HasValue && !await _unitOfWork.Repository<BatchEnrollment>().ExistsAsync(
                        e => e.ChildId == childId && e.BatchId == request.BatchId.Value && e.Status == EnrollmentStatus.Active, cancellationToken))
                {
                    await _batchService.AssignStudentAsync(request.BatchId.Value, childId, cancellationToken);
                }
            }
            else
            {
                // The enrollment form the parent would have filled in, filled in by staff, then
                // approved through the normal review so every downstream step is identical.
                var form = await _unitOfWork.Repository<EnrollmentForm>()
                    .FirstOrDefaultAsync(f => f.DemoBookingId == booking.Id && f.Status == EnrollmentFormStatus.Submitted, cancellationToken);
                if (form is null)
                {
                    form = new EnrollmentForm
                    {
                        ParentProfileId = parentProfile.Id,
                        DemoBookingId = booking.Id,
                        Status = EnrollmentFormStatus.Submitted,
                        SubmittedAtUtc = DateTime.UtcNow,
                        FormDataJson = JsonSerializer.Serialize(new
                        {
                            childName = $"{request.ChildFirstName.Trim()} {request.ChildLastName?.Trim()}".Trim(),
                            parentName = request.ParentName.Trim(),
                            parentPhone = phone,
                            childDateOfBirth = request.ChildDateOfBirth.ToString("yyyy-MM-dd"),
                            enteredByStaff = true,
                        }),
                    };
                    await _unitOfWork.Repository<EnrollmentForm>().AddAsync(form, cancellationToken);
                    await _auditLog.StageAsync(AuditAction.Create, nameof(EnrollmentForm), form.Id.ToString(),
                        "{\"note\":\"Manual admission after demo\"}", cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }

                var reviewed = await _enrollmentService.ReviewAsync(
                    form.Id,
                    new ReviewEnrollmentFormRequest
                    {
                        Approve = true,
                        ChildFirstName = request.ChildFirstName.Trim(),
                        ChildLastName = request.ChildLastName?.Trim(),
                        ChildDateOfBirth = request.ChildDateOfBirth,
                        PackagePlanId = request.PackagePlanId,
                        PackagePlanPriceOverride = request.Amount,
                        BatchId = request.BatchId,
                    },
                    cancellationToken);
                childId = reviewed.ChildId
                    ?? throw new InvalidOperationException("Approved enrollment form has no child.");
            }

            var alreadyInvoiced = priorForm is not null && await _unitOfWork.Repository<Invoice>().ExistsAsync(
                i => i.ChildId == childId && i.Status != InvoiceStatus.Cancelled, cancellationToken);
            if (course is not null && feeAmount > 0 && !alreadyInvoiced)
            {
                // Course fee: a one-off invoice, the same thing staff create by hand in Billing.
                await _billingService.CreateInvoiceAsync(
                    new CreateInvoiceRequest
                    {
                        ParentProfileId = parentProfile.Id,
                        ChildId = childId,
                        CourseId = course.Id,
                        DepartmentId = course.DepartmentId,
                        Amount = feeAmount,
                        DueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(FeeDueDays),
                    },
                    cancellationToken);
            }

            var invoice = await _unitOfWork.Repository<Invoice>().Query()
                .Where(i => i.ChildId == childId && i.Status != InvoiceStatus.Cancelled)
                .OrderByDescending(i => i.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            // A plan agreed at ₹0: don't leave a Pending ₹0 invoice that would go overdue.
            if (plan is not null && invoice is not null && invoice.Amount == 0 && invoice.Status != InvoiceStatus.Paid)
            {
                invoice.Status = InvoiceStatus.Paid;
                invoice.PaidAtUtc = DateTime.UtcNow;
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            string? paymentLinkUrl = null;
            string? paymentLinkError = null;
            var amountDue = invoice is null ? 0 : invoice.Amount - invoice.AmountPaid;
            if (invoice is not null && amountDue > 0)
            {
                try
                {
                    paymentLinkUrl = (await _billingService.CreatePaymentLinkAsync(invoice.Id, cancellationToken)).Url;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Admission itself is done; the parent can still pay from the portal and
                    // staff can retry the link from Payments. Don't undo a completed admission.
                    _logger.LogWarning(ex, "Payment link failed for manual admission invoice {InvoiceId}", invoice.Id);
                    paymentLinkError = ex is DomainValidationException or NotFoundException
                        ? ex.Message
                        : "The payment gateway couldn't create a link right now. The parent can still pay from the portal.";
                }
            }

            // ReviewAsync marks the demo Enrolled; until the fee is in it's really PaymentPending.
            var tracked = await _unitOfWork.Repository<DemoBooking>()
                .FirstOrDefaultAsync(b => b.Id == booking.Id, cancellationToken);
            if (tracked is not null)
            {
                tracked.InvoiceId = invoice?.Id;
                tracked.PaymentLinkUrl = paymentLinkUrl;
                tracked.ConversionStatus = amountDue > 0 ? ConversionStatus.PaymentPending : ConversionStatus.Enrolled;
                _unitOfWork.Repository<DemoBooking>().Update(tracked);
                await _auditLog.StageAsync(AuditAction.Update, nameof(DemoBooking), booking.Id.ToString(),
                    "{\"note\":\"Admitted manually\"}", cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            var loginId = ParentLogin.IsPlaceholderEmail(parent.Email) ? (phone ?? parent.Phone ?? string.Empty) : parent.Email;
            var loginUrl = (_configuration["Frontend:BaseUrl"] ?? "https://thereadernest.in").TrimEnd('/') + "/login";
            var currency = invoice?.Currency ?? "INR";

            return new ManualAdmissionResultDto
            {
                Booking = await _demoBookingService.GetAsync(booking.Id, cancellationToken),
                ParentUserId = parent.Id,
                ChildId = childId,
                LoginId = loginId,
                TemporaryPin = temporaryPin,
                LoginUrl = loginUrl,
                InvoiceId = invoice?.Id,
                InvoiceNumber = invoice?.InvoiceNumber,
                AmountDue = amountDue,
                Currency = currency,
                PaymentLinkUrl = paymentLinkUrl,
                PaymentLinkError = paymentLinkError,
                WhatsAppMessage = BuildWhatsAppMessage(
                    request.ParentName.Trim(), request.ChildFirstName.Trim(), plan?.Name ?? course!.Name,
                    loginUrl, loginId, temporaryPin, amountDue, currency, paymentLinkUrl),
            };
        }

        public async Task<ManualAdmissionOptionsDto> GetOptionsAsync(CancellationToken cancellationToken = default)
        {
            var plans = await _unitOfWork.Repository<PackagePlan>().Query()
                .Where(p => p.IsActive)
                .OrderBy(p => p.Name)
                .Select(p => new ManualAdmissionPlanOption
                {
                    Id = p.Id,
                    Name = p.Name,
                    CourseName = p.Course != null ? p.Course.Name : null,
                    Price = p.Price,
                })
                .ToListAsync(cancellationToken);

            var batches = await _unitOfWork.Repository<Batch>().Query()
                .Where(b => b.Status == BatchStatus.Active)
                .OrderBy(b => b.Name)
                .Select(b => new ManualAdmissionBatchOption
                {
                    Id = b.Id,
                    CourseId = b.CourseId,
                    Name = b.Name,
                    CourseName = b.Course.Name,
                    TeacherName = b.TeacherProfile.User.FirstName + " " + b.TeacherProfile.User.LastName,
                    SeatsLeft = b.Capacity - b.Enrollments.Count(e => e.Status == EnrollmentStatus.Active),
                })
                .ToListAsync(cancellationToken);

            var courses = await _unitOfWork.Repository<Course>().Query()
                .Where(c => c.IsActive)
                .OrderBy(c => c.Name)
                .Select(c => new ManualAdmissionCourseOption
                {
                    Id = c.Id,
                    Name = c.Name,
                    DepartmentName = c.Department.Name,
                    Price = c.Price,
                })
                .ToListAsync(cancellationToken);

            return new ManualAdmissionOptionsDto { Plans = plans, Courses = courses, Batches = batches };
        }

        private static string BuildWhatsAppMessage(
            string parentName, string childName, string planName, string loginUrl, string loginId,
            string? pin, decimal amountDue, string currency, string? paymentLinkUrl)
        {
            var lines = new List<string>
            {
                $"Hello {parentName},",
                "",
                $"Welcome to The Reader Nest! {childName}'s admission for {planName} is done.",
                "",
                "Parent portal login:",
                loginUrl,
                $"Login: {loginId}",
                pin is null ? "PIN: your existing PIN" : $"PIN: {pin}",
            };
            if (amountDue > 0)
            {
                lines.Add("");
                lines.Add($"Fee due: {currency} {amountDue:0.##}");
                lines.Add(paymentLinkUrl is null
                    ? "You can pay from the portal under Payments."
                    : $"Pay here: {paymentLinkUrl}");
                lines.Add("You can also pay in parts from the portal (Payments → Pay Now).");
            }
            lines.Add("");
            lines.Add("Thank you!");
            return string.Join("\n", lines);
        }
    }
}
