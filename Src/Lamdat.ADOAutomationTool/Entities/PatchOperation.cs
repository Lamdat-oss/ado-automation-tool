using Microsoft.CodeAnalysis;
using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace Lamdat.ADOAutomationTool.Entities
{
    public class JsonPatchOperation
    {
        /// <summary>
        // The type of operation (e.g., "add", "replace", "remove")
        /// </summary>
        [JsonProperty("op")]
        public string Operation { get; set; }

        /// <summary>
        /// The JSON Pointer path to the target location within the JSON document
        /// </summary>
        public string Path { get; set; }      

        /// <summary>
        /// The value to be applied(used for "add" and "replace" operations)
        /// </summary>
        public object Value { get; set; }     
    }
}
