using Newtonsoft.Json;

namespace Lamdat.ADOAutomationTool.Entities
{
    public class WebHookInfo
    {
        [JsonProperty("eventType")]
        public string EventType { get; set; }

        [JsonProperty("resource")]
        public WebHookResource Resource { get; set; }

        public string? Project { get { return ResourceContainers?.project?.id?.ToString(); } }

        [JsonProperty("resourceContainers")]
        public dynamic ResourceContainers { get; set; }


    }




}

