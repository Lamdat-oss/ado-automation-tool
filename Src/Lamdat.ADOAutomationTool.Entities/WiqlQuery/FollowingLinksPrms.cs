namespace Lamdat.ADOAutomationTool.Entities
{
    public class FollowingLinksPrms
    {
        /// <summary>
        /// Name of link type
        /// </summary>
        public string LinkType { get; set; }
         
        /// <summary>
        /// Entity type of linked entity
        /// </summary>
        public string WhereTypeIs { get; set; }
         
        /// <summary>
        /// Optional. Maximum amount of linked entities
        /// </summary>
        public int? AtMost { get; set; }
    }   
}