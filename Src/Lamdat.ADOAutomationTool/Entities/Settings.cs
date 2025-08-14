namespace Lamdat.ADOAutomationTool.Entities
{
    public class Settings
    {
        public string CollectionURL { get; set; }

        public string PAT { get; set; }

        public bool BypassRules { get; set; } = true;

        public string SharedKey { get; set; }

        public string AllowedCorsOrigin { get; set; }

        public bool NotValidCertificates { get; set; } = false;

        public bool EnableAutoHttpsRedirect { get; set; } = true;

        public double MemoryCleanupMinutes { get; set; } = 2;

        public double ScheduledTaskIntervalMinutes { get; set; } = 5;

        public RulesStorageType RulesStorageType { get; set; } = RulesStorageType.Disk;

        public string? S3BucketName { get; set; }

        public string? S3AccessKey { get; set; }

        public string? S3SecretKey { get; set; }

        public string? S3Endpoint { get; set; }

        public string? S3FolderPath { get; set; }

        public string? S3StorageRegion { get; set; }

        public int ScriptExecutionTimeoutSeconds { get; set; }

        public int MaxQueueWebHookRequestCount { get; set; } = 1000;

        /// <summary>
        /// Default last run date for scheduled scripts on first execution after system restart.
        /// If not specified, defaults to current time when script first runs.
        /// Format: ISO 8601 date string (e.g., "2024-01-01T00:00:00Z") or number of days ago (e.g., "7" for 7 days ago)
        /// </summary>
        public string? ScheduledScriptDefaultLastRun { get; set; }
    }
}
