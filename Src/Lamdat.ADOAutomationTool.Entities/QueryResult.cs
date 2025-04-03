using Newtonsoft.Json;

namespace Lamdat.ADOAutomationTool.Entities
{
    public class QueryResult<T>
    {
        [JsonProperty("count")]
        public int Count { get; set; }

        [JsonProperty("value")]
        public List<T> Value { get; set; }
       
    }
}