using Newtonsoft.Json;

namespace Lamdat.ADOAutomationTool.Entities
{
    public class IterationDetailsResponse
    {
        [JsonProperty("value")]
        public IterationDetails[] Value { get; set; }
    }
}
