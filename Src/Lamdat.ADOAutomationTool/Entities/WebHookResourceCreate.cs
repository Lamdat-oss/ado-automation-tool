using Newtonsoft.Json;

namespace Lamdat.ADOAutomationTool.Entities
{
    public class WebHookResourceCreate : WebHookResourceBase
    {
        
        [JsonProperty("relations")]
        public List<RelationAddedRemoved> Relations { get; set; } = new List<RelationAddedRemoved>();


    }
}
