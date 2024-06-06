using Newtonsoft.Json;

namespace Lamdat.ADOAutomationTool.Entities
{
    public class RevisionResponse
    {
        [JsonProperty("value")]
        public List<Revision> Value { get; set; }
    }

    public class Revision
    {
        [JsonProperty("fields")]
        public RevisionFields Fields { get; set; }
    }

    public class RevisionFields
    {
        [JsonProperty("System.ChangedDate")]
        public DateTime SystemChangedDate { get; set; }

        [JsonProperty("System.ChangedBy")]
        public ADOUserRevision SystemChangedBy { get; set; }
    }

    public class ADOUserRevision
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

        [JsonProperty("uniqueName")]
        public string UniqueName { get; set; }

        [JsonProperty("emailAddress")]
        public string EmailAddress { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("imageUrl")]
        public string ImageUrl { get; set; }
    }

}
