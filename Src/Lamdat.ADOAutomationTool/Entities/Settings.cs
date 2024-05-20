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
    }
}
