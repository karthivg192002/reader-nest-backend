using iucs.readernest.application.Dto.Common;
using iucs.readernest.application.Dto.Courses;

namespace iucs.readernest.application.Services
{
    public interface IDepartmentService
    {
        Task<IReadOnlyList<DepartmentDto>> ListAsync(bool includeInactive = false, CancellationToken cancellationToken = default);

        Task<DepartmentDto> CreateAsync(SaveDepartmentRequest request, CancellationToken cancellationToken = default);

        Task<DepartmentDto> UpdateAsync(Guid id, SaveDepartmentRequest request, CancellationToken cancellationToken = default);

        /// <summary>Row-by-row: one bad row is recorded as a failure and never aborts the rest. Columns: Name, Description, IsActive.</summary>
        Task<BulkImportResult> BulkImportAsync(Stream file, string fileName, CancellationToken cancellationToken = default);

        Task<string> ExportCsvAsync(bool includeInactive, CancellationToken cancellationToken = default);
    }
}
