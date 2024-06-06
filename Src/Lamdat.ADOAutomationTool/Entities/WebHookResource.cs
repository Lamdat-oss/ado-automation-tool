using Newtonsoft.Json;

namespace Lamdat.ADOAutomationTool.Entities
{
    public class WebHookResource
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("workItemId")]
        public int WorkItemId { get; set; }


        [JsonProperty("rev")]
        public int Revision { get; set; }


        [JsonProperty("fields")]
        public Dictionary<string, object> Fields { get; set; } = new Dictionary<string, object>();

        [JsonProperty("relations")]
        public Relations Relations { get; set; } = new Relations();


    }
}
