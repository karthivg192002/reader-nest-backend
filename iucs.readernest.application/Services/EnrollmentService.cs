using System.Text.Json;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Enrollment;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class EnrollmentService : IEnrollmentService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;

        public EnrollmentService(IUnitOfWork unitOfWork, IAuditLogService auditLog)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
        }

        public async Task<EnrollmentFormDto> SubmitAsync(
            Guid parentUserId,
            SubmitEnrollmentFormRequest request,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var _ = JsonDocument.Parse(request.FormDataJson);
            }
            catch (JsonException)
            {
                throw new DomainValidationException("The submitted form data is not valid JSON.");
            }

            var parent = await GetParentAsync(parentUserId, cancellationToken);

            // A rejected form is resubmitted in place; otherwise every submission is a new child enrollment
            var form = await _unitOfWork.Repository<EnrollmentForm>().FirstOrDefaultAsync(
                f => f.ParentProfileId == parent.Id && f.Status == EnrollmentFormStatus.Rejected,
                cancellationToken);

            if (form is null)
            {
                form = new EnrollmentForm { ParentProfileId = parent.Id };
                await _unitOfWork.Repository<EnrollmentForm>().AddAsync(form, cancellationToken);
            }

            form.FormDataJson = request.FormDataJson;
            form.Status = EnrollmentFormStatus.Submitted;
            form.SubmittedAtUtc = DateTime.UtcNow;

            await _auditLog.StageAsync(AuditAction.Create, nameof(EnrollmentForm), form.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return await GetAsync(form.Id, cancellationToken);
        }

        public async Task<IReadOnlyList<EnrollmentFormDto>> ListForParentUserAsync(
            Guid parentUserId,
            CancellationToken cancellationToken = default)
        {
            var parent = await GetParentAsync(parentUserId, cancellationToken);
            var forms = await BaseQuery()
                .Where(f => f.ParentProfileId == parent.Id)
                .OrderByDescending(f => f.CreatedAtUtc)
                .ToListAsync(cancellationToken);
            return forms.Select(ToDto).ToList();
        }

        public async Task<IReadOnlyList<EnrollmentFormDto>> ListAsync(
            EnrollmentFormStatus? status,
            CancellationToken cancellationToken = default)
        {
            IQueryable<EnrollmentForm> query = BaseQuery();
            if (status.HasValue)
            {
                query = query.Where(f => f.Status == status.Value);
            }

            var forms = await query.OrderByDescending(f => f.SubmittedAtUtc).ToListAsync(cancellationToken);
            return forms.Select(ToDto).ToList();
        }

        public async Task<EnrollmentFormDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var form = await BaseQuery().FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(EnrollmentForm), id);
            return ToDto(form);
        }

        public async Task<EnrollmentFormDto> ReviewAsync(
            Guid id,
            ReviewEnrollmentFormRequest request,
            CancellationToken cancellationToken = default)
        {
            var form = await BaseQuery().FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(EnrollmentForm), id);

            if (form.Status != EnrollmentFormStatus.Submitted)
            {
                throw new DomainValidationException($"Only a submitted form can be reviewed; this one is {form.Status}.");
            }

            if (request.Approve)
            {
                var (firstName, lastName) = ResolveChildName(form, request);
                var child = new Child
                {
                    ParentProfileId = form.ParentProfileId,
                    FirstName = firstName,
                    LastName = lastName,
                    DateOfBirth = request.ChildDateOfBirth,
                };
                await _unitOfWork.Repository<Child>().AddAsync(child, cancellationToken);

                form.Child = child;
                form.Status = EnrollmentFormStatus.Approved;
                form.ParentProfile.EnrollmentFormCompleted = true;
            }
            else
            {
                form.Status = EnrollmentFormStatus.Rejected;
            }

            form.ReviewedAtUtc = DateTime.UtcNow;

            await _auditLog.StageAsync(AuditAction.Update, nameof(EnrollmentForm), form.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return await GetAsync(form.Id, cancellationToken);
        }

        public async Task<IReadOnlyList<ChildDto>> ListChildrenForParentUserAsync(
            Guid parentUserId,
            CancellationToken cancellationToken = default)
        {
            var parent = await GetParentAsync(parentUserId, cancellationToken);
            var children = await _unitOfWork.Repository<Child>().Query()
                .Where(c => c.ParentProfileId == parent.Id)
                .OrderBy(c => c.FirstName)
                .ToListAsync(cancellationToken);

            return children.Select(c => new ChildDto
            {
                Id = c.Id,
                ParentProfileId = c.ParentProfileId,
                FirstName = c.FirstName,
                LastName = c.LastName,
                DateOfBirth = c.DateOfBirth,
                AcademicLevel = c.AcademicLevel,
                IsActive = c.IsActive,
            }).ToList();
        }

        private static (string FirstName, string LastName) ResolveChildName(EnrollmentForm form, ReviewEnrollmentFormRequest request)
        {
            if (!string.IsNullOrWhiteSpace(request.ChildFirstName))
            {
                return (request.ChildFirstName.Trim(), request.ChildLastName?.Trim() ?? "");
            }

            // Fall back to the form's own answer (proposed field id: child full name)
            using var document = JsonDocument.Parse(form.FormDataJson);
            if (document.RootElement.TryGetProperty("childName", out var nameElement)
                && nameElement.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(nameElement.GetString()))
            {
                var parts = nameElement.GetString()!.Trim().Split(' ', 2);
                return (parts[0], parts.Length > 1 ? parts[1] : "");
            }

            throw new DomainValidationException(
                "Provide the child's name for approval: the form has no 'childName' answer to fall back on.");
        }

        private async Task<ParentProfile> GetParentAsync(Guid parentUserId, CancellationToken cancellationToken)
        {
            return await _unitOfWork.Repository<ParentProfile>()
                .FirstOrDefaultAsync(p => p.UserId == parentUserId, cancellationToken)
                ?? throw new NotFoundException("No parent profile is linked to the current account.");
        }

        private IQueryable<EnrollmentForm> BaseQuery()
        {
            return _unitOfWork.Repository<EnrollmentForm>().Query()
                .Include(f => f.ParentProfile).ThenInclude(p => p.User);
        }

        private static EnrollmentFormDto ToDto(EnrollmentForm form)
        {
            var parentUser = form.ParentProfile.User;
            return new EnrollmentFormDto
            {
                Id = form.Id,
                ParentProfileId = form.ParentProfileId,
                ParentName = $"{parentUser.FirstName} {parentUser.LastName}",
                ParentEmail = parentUser.Email,
                ChildId = form.ChildId,
                FormDataJson = form.FormDataJson,
                Status = form.Status,
                SubmittedAtUtc = form.SubmittedAtUtc,
                ReviewedAtUtc = form.ReviewedAtUtc,
            };
        }
    }
}
