namespace iucs.readernest.application.Common
{
    /// <summary>
    /// Where uploaded resources and class presentations are kept, chosen per client on
    /// Admin → Settings → File storage and stored as the "storage.provider" AppSetting ("S3" or
    /// "Local"). Without the row, the Storage:Provider configuration value decides. Only the File
    /// storage card changes it (StorageController), never the generic settings save.
    /// </summary>
    public static class StorageSettings
    {
        public const string ProviderKey = "storage.provider";

        public const string S3 = "S3";

        public const string Local = "Local";
    }
}
