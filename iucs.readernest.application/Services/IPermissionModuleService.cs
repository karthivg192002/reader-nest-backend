using iucs.readernest.application.Dto.Users;

namespace iucs.readernest.application.Services
{
    /// <summary>
    /// The grantable permission-module catalog: the 12 built-in modules (seeded, IsSystem =
    /// true) plus any an Admin defines from the UI. See PermissionModuleDefinition's own doc
    /// comment for what a custom module can and can't gate.
    /// </summary>
    public interface IPermissionModuleService
    {
        Task<IReadOnlyList<PermissionModuleDefinitionDto>> ListAsync(CancellationToken cancellationToken = default);

        Task<PermissionModuleDefinitionDto> CreateAsync(SavePermissionModuleDefinitionRequest request, CancellationToken cancellationToken = default);

        Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    }
}
