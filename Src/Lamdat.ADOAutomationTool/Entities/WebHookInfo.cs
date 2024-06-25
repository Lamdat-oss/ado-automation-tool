using Newtonsoft.Json;

namespace Lamdat.ADOAutomationTool.Entities
{
    public class WebHookInfo<T>
    {
        [JsonProperty("eventType")]
        public string EventType { get; set; }

        [JsonProperty("resource")]
        public T Resource { get; set; }


        public string? Project
        {
            set { value = ResourceContainers?.project?.id?.ToString(); }
            get { return ResourceContainers?.project?.id?.ToString(); }
        }

        [JsonProperty("resourceContainers")]
        public dynamic ResourceContainers { get; set; }


    }




}

