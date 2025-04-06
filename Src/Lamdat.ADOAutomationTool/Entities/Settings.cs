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
        
        public RulesStorageType RulesStorageType { get; set; } = RulesStorageType.Disk; 
        
        public string? S3BucketName { get; set; }
        
        public string? S3AccessKey { get; set; }
        
        public string? S3SecretKey { get; set; }
        
        public string? S3Endpoint { get; set; }
        
        public string? S3FolderPath { get; set; }
        
        public string? S3StorageRegion { get; set; }
    }
}
