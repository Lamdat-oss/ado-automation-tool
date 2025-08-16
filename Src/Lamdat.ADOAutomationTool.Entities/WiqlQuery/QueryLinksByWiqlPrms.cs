namespace Lamdat.ADOAutomationTool.Entities
{
    public class QueryLinksByWiqlPrms
    {
        /// <summary>
        /// The ID of an entity for which to search linked entities
        /// </summary>
        public int SourceWorkItemId { get; set; }
        
        /// <summary>
        /// The Type of an entity for which to search linked entities
        /// </summary>
        public string SourceWorkItemType { get; set; }
        
        /// <summary>
        /// Name of link type
        /// </summary>
        public string LinkType { get; set; }
        
        /// <summary>
        /// WIQL query string for work item queries
        /// </summary>
        public string Wiql { get; set; }
        
        /// <summary>
        /// Linked entity type
        /// </summary>
        public string? TargetWorkItemType { get; set; }
        
        /// <summary>
        /// Optional. Maximum amount of linked entities. Default 200
        /// </summary>
        public int? Top { get; set; }
        
        /// <summary>
        /// Optional. Which exactly fields to query 
        /// </summary>
        public List<string> Fields { get; set; }
    }
}