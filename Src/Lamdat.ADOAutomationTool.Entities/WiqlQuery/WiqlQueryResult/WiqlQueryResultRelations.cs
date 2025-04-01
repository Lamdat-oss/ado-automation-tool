using Newtonsoft.Json;

namespace Lamdat.ADOAutomationTool.Entities
{
    /// <summary>
    /// Work item details from API response
    /// </summary>
    /// <example>
    /// {
    ///     "rel": "System.LinkTypes.Hierarchy-Forward",
    ///     "source": {
    ///         "id": 1,
    ///         "url": "{AzureDevOpsCollection}/_apis/wit/workItems/1"
    ///     },
    ///     "target": {
    ///         "id": 5,
    ///         "url": "{AzureDevOpsCollection}/_apis/wit/workItems/5"
    ///     }
    /// }
    /// </example>
    public class WiqlQueryResultRelations
    {
        [JsonProperty("rel")]
        public string Rel { get; set; }
        
        [JsonProperty("source")]
        public WiqlQueryResultWorkItem? Source { get; set; }
        
        [JsonProperty("target")]
        public WiqlQueryResultWorkItem? Target { get; set; }
    }    
}
