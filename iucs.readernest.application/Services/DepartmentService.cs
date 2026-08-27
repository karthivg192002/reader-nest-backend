using iucs.readernest.application.Common;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Dto.Common;
using iucs.readernest.application.Dto.Courses;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Billing;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class DepartmentService : IDepartmentService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;
        private readonly IBulkFileReader _bulkFileReader;

        public DepartmentService(IUnitOfWork unitOfWork, IAuditLogService auditLog, IBulkFileReader bulkFileReader)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
            _bulkFileReader = bulkFileReader;
        }

        public async Task<IReadOnlyList<DepartmentDto>> ListAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
        {
            var query = _unitOfWork.Repository<Department>().Query();
            if (!includeInactive)
            {
                query = query.Where(d => d.IsActive);
            }

            var departments = await query.OrderBy(d => d.Name).ToListAsync(cancellationToken);
            return departments.Select(ToDto).ToList();
        }

        public async Task<DepartmentDto> CreateAsync(SaveDepartmentRequest request, CancellationToken cancellationToken = default)
        {
            var name = request.Name.Trim();
            var repository = _unitOfWork.Repository<Department>();

            if (await repository.ExistsAsync(d => d.Name == name, cancellationToken))
            {
                throw new ConflictException($"A department named '{name}' already exists.");
            }

            var department = new Department
            {
                Name = name,
                Description = request.Description,
                IsActive = request.IsActive,
            };
            await repository.AddAsync(department, cancellationToken);

            // Every department needs its own payment account row (PaymentAccount.DepartmentId
            // is uniquely indexed) for invoices to route anywhere — without this, a newly-added
            // department was invisible on Payment Gateway Mapping and had nothing to route
            // through until someone created its account by hand. Most orgs here run one real
            // gateway account for the whole business, not a distinct one per department, so a
            // new department inherits whichever real (non-placeholder) account was configured
            // first — active immediately, nothing to set up. Only when literally nothing has
            // ever been configured yet does it fall back to its own inactive placeholder (the
            // same "pending-client-decision" convention PaymentMapping.tsx already recognizes
            // and clears back to a blank field the moment someone opens it to configure).
            var existingRealAccount = await _unitOfWork.Repository<PaymentAccount>().Query()
                .Where(a => a.GatewayAccountRef != "pending-client-decision")
                .OrderBy(a => a.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            await _unitOfWork.Repository<PaymentAccount>().AddAsync(
                new PaymentAccount
                {
                    Name = $"{name} Department Account",
                    DepartmentId = department.Id,
                    GatewayProvider = existingRealAccount?.GatewayProvider ?? "razorpay",
                    GatewayAccountRef = existingRealAccount?.GatewayAccountRef ?? "pending-client-decision",
                    IsActive = existingRealAccount is not null,
                },
                cancellationToken);

            await _auditLog.StageAsync(AuditAction.Create, nameof(Department), department.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(department);
        }

        public async Task<DepartmentDto> UpdateAsync(Guid id, SaveDepartmentRequest request, CancellationToken cancellationToken = default)
        {
            var repository = _unitOfWork.Repository<Department>();
            var department = await repository.GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(Department), id);

            var name = request.Name.Trim();
            if (!string.Equals(department.Name, name, StringComparison.Ordinal)
                && await repository.ExistsAsync(d => d.Name == name && d.Id != id, cancellationToken))
            {
                throw new ConflictException($"A department named '{name}' already exists.");
            }

            department.Name = name;
            department.Description = request.Description;
            department.IsActive = request.IsActive;
            repository.Update(department);

            await _auditLog.StageAsync(AuditAction.Update, nameof(Department), department.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDto(department);
        }

        public async Task<BulkImportResult> BulkImportAsync(Stream file, string fileName, CancellationToken cancellationToken = default)
        {
            var rows = _bulkFileReader.ReadRows(file, fileName);
            var result = new BulkImportResult { TotalRows = rows.Count };

            for (var i = 0; i < rows.Count; i++)
            {
                var rowNumber = i + 2; // header is row 1
                try
                {
                    var row = rows[i];
                    var name = row.GetOrNull("Name")
                        ?? throw new DomainValidationException("Name is required.");

                    await CreateAsync(
                        new SaveDepartmentRequest
                        {
                            Name = name,
                            Description = row.GetOrNull("Description"),
                            IsActive = row.GetBool("IsActive"),
                        },
                        cancellationToken);
                    result.SucceededCount++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    result.FailedCount++;
                    result.Errors.Add(new BulkImportRowError { RowNumber = rowNumber, Message = ex.Message });
                }
            }

            return result;
        }

        public async Task<string> ExportCsvAsync(bool includeInactive, CancellationToken cancellationToken = default)
        {
            var departments = await ListAsync(includeInactive, cancellationToken);
            string[] headers = ["Name", "Description", "IsActive"];
            var rows = departments.Select(d => new List<string?> { d.Name, d.Description, d.IsActive ? "true" : "false" });
            return CsvWriter.BuildCsv(headers, rows);
        }

        private static DepartmentDto ToDto(Department department) => new()
        {
            Id = department.Id,
            Name = department.Name,
            Description = department.Description,
            IsActive = department.IsActive,
        };
    }
}
