using Newtonsoft.Json;

namespace Lamdat.ADOAutomationTool.Entities
{
    public class WorkItemFields
    {
        [JsonProperty("System.Id")]
        public int WorkItemId { get; set; }

        [JsonProperty("System.Title")]
        public string Title { get; set; }

        [JsonProperty("System.State")]
        public string State { get; set; }

    }
}

