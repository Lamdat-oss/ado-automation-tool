using Newtonsoft.Json;

namespace Lamdat.ADOAutomationTool.Entities
{
    /// <summary>
    /// The response of POST API {AzureDevOpsCollection}/_apis/wit/wiql
    /// </summary>
    /// <example>
    /// {
    ///    "workItems": [
    ///         {
    ///             "id": 300,
    ///             "url": "https://dev.azure.com/fabrikam/_apis/wit/workItems/300"
    ///         },
    ///         {
    ///             "id": 299,
    ///             "url": "https://dev.azure.com/fabrikam/_apis/wit/workItems/299"
    ///         }
    ///    ]
    /// }
    /// </example>
    /// <example>
    /// {
    ///    "workItemRelations":
    ///     [
    ///         {
    ///             "rel": "System.LinkTypes.Hierarchy-Forward",
    ///             "source": {
    ///                 "id": 1,
    ///                 "url": "{AzureDevOpsCollection}/_apis/wit/workItems/1"
    ///             },
    ///             "target": {
    ///                 "id": 5,
    ///                 "url": "{AzureDevOpsCollection}/_apis/wit/workItems/5"
    ///             }
    ///         }
    ///     ]
    /// }
    /// </example>
    public class WiqlQueryResult
    {
        /// <summary>
        /// Property is set for WIQL "Select ... From WorkItems ..."
        /// </summary>
        [JsonProperty("workItems")]
        public List<WiqlQueryResultWorkItem> WorkItems { get; set; }
        
        
        /// <summary>
        /// Property is set for WIQL "Select ... From workitemLinks ..."
        /// </summary>
        [JsonProperty("workItemRelations")]
        public List<WiqlQueryResultRelations> WorkItemRelations { get; set; }
    }  
}