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

        [JsonProperty("teamName")]
        public string TeamName { get; set; }

        [JsonProperty("startDate")]
        public DateTime? StartDate { get; set; }

        [JsonProperty("endDate")]
        public DateTime? EndDate { get; set; }
    }

    public class IterationAttributes
    {
        [JsonProperty("startDate")]
        public DateTime? StartDate { get; set; }

        [JsonProperty("finishDate")]
        public DateTime? FinishDate { get; set; }
    }
}
