using Newtonsoft.Json;

namespace Lamdat.ADOAutomationTool.Entities
{
    public class WebHookResourceUpdate : WebHookResourceBase
    {


        [JsonProperty("relations")]
        public Relations Relations { get; set; } = new Relations();


    }
}
