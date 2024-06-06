using Newtonsoft.Json;

namespace Lamdat.ADOAutomationTool.Entities
{   
    public class Attributes
    {
        [JsonProperty("isLocked")]
        public bool IsLocked { get; set; }
        
        [JsonProperty("name")]
        public string Name { get; set; }
    }

    public class RelationAddedRemoved
    {
        [JsonProperty("attributes")]
        public Attributes Attributes { get; set; }
        
        [JsonProperty("rel")]
        public string Rel { get; set; }
        
        [JsonProperty("url")]
        public string Url { get; set; }
    }

    public class Relations
    {
        [JsonProperty("removed")]
        public List<RelationAddedRemoved> Removed { get; set; }

        [JsonProperty("added")]
        public List<RelationAddedRemoved> Added { get; set; }
    }
}
