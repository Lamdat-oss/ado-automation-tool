using Newtonsoft.Json;

namespace Lamdat.ADOAutomationTool.Entities
{
    /// <summary>
    /// Work item details from API response
    /// </summary>
    /// <example>
    /// {
    ///     "id": 300,
    ///     "url": "https://dev.azure.com/fabrikam/_apis/wit/workItems/300"
    /// }
    /// </example>
    public class WiqlQueryResultWorkItem
    {
        [JsonProperty("id")]
        public int ID { get; set; }
        
        [JsonProperty("url")]
        public string Url { get; set; }
    }   
}