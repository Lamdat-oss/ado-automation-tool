namespace Lamdat.ADOAutomationTool.Entities
{
    public class Identity
    {
        public string IdentityType { get; set; }
        public string FriendlyDisplayName { get; set; }
        public string DisplayName { get; set; }
        public string SubHeader { get; set; }
        public string TeamFoundationId { get; set; }
        public string EntityId { get; set; }
        public string Domain { get; set; }
        public string AccountName { get; set; }
        public bool IsWindowsUser { get; set; }
        public string MailAddress { get; set; }
    }
}
