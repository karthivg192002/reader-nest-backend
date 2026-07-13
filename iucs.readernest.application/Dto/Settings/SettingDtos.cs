using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Settings
{
    public class SettingDto
    {
        public SettingCategory Category { get; set; }

        public string Key { get; set; } = null!;

        public string? Value { get; set; }

        public bool IsPublic { get; set; }
    }

    /// <summary>Single key update; unknown keys are created under the given category.</summary>
    public class UpdateSettingRequest
    {
        public string Key { get; set; } = null!;

        public string? Value { get; set; }

        public SettingCategory Category { get; set; } = SettingCategory.General;

        public bool IsPublic { get; set; }
    }
}
