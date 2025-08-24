using Newtonsoft.Json;

namespace Lamdat.ADOAutomationTool.Entities
{
    /// <summary>
    /// Represents a JSON Patch operation for Azure DevOps API calls
    /// </summary>
    public class JsonPatchOperation
    {
        [JsonProperty("op")]
        public string Operation { get; set; } = string.Empty;

        [JsonProperty("path")]
        public string Path { get; set; } = string.Empty;

        [JsonProperty("value")]
        public object? Value { get; set; }

        [JsonProperty("from")]
        public string? From { get; set; }
    }
}