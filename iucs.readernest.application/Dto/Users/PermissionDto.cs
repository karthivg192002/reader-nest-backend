using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Users
{
    public class PermissionDto
    {
        public PermissionModule Module { get; set; }

        public bool CanView { get; set; }

        public bool CanCreate { get; set; }

        public bool CanEdit { get; set; }

        public bool CanDelete { get; set; }

        public bool CanApprove { get; set; }
    }
}
