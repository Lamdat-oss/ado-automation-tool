using Newtonsoft.Json;
using System;

namespace Lamdat.ADOAutomationTool.Entities
{
    public class IterationDetails
    {
        [JsonProperty("id")]
        public Guid Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("attributes")]
        public IterationAttributes Attributes { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        // Additional properties for testing support
        public string TeamName { get; set; }
        
        // Convenience properties that delegate to Attributes
        public DateTime? StartDate 
        { 
            get => Attributes?.StartDate; 
            set 
            { 
                if (Attributes == null) Attributes = new IterationAttributes();
                Attributes.StartDate = value;
            }
        }
        
        public DateTime? EndDate 
        { 
            get => Attributes?.FinishDate; 
            set 
            { 
                if (Attributes == null) Attributes = new IterationAttributes();
                Attributes.FinishDate = value;
            }
        }
    }

    public class IterationAttributes
    {
        [JsonProperty("startDate")]
        public DateTime? StartDate { get; set; }

        [JsonProperty("finishDate")]
        public DateTime? FinishDate { get; set; }
    }
}
