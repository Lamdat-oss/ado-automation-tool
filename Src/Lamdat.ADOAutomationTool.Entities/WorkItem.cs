using Newtonsoft.Json;

namespace Lamdat.ADOAutomationTool.Entities
{
    public class WorkItem
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("rev")]
        public int Revision { get; set; }

        [JsonProperty("fields")]
        public Dictionary<string, object> Fields { get; set; } = new Dictionary<string, object>();

        public string Title
        {
            get { return Fields.ContainsKey("System.Title") ? Fields["System.Title"]?.ToString() : ""; }
            set
            {
                if (Fields.ContainsKey("System.Title"))
                    Fields["System.Title"] = value;
                else
                    Fields.Add("System.Title", value);
            }
        }

        public string WorkItemType
        {
            get { return Fields.ContainsKey("System.WorkItemType") ? Fields["System.WorkItemType"]?.ToString() : ""; }
        }

        public string State
        {
            get { return Fields.ContainsKey("System.State") ? Fields["System.State"]?.ToString() : ""; }
            set
            {
                if (Fields.ContainsKey("System.State"))
                    Fields["System.State"] = value;
                else
                    Fields.Add("System.State", value);
            }
        }

        public string Project { get { return Fields.ContainsKey("System.TeamProject") ? Fields["System.TeamProject"]?.ToString() : ""; } }

        public WorkItemRelation Parent { get; set; }

        public List<WorkItemRelation> Children { get; set; } = new List<WorkItemRelation>();

        [JsonProperty("relations")]
        public dynamic relations { get; private set; }


        public List<WorkItemRelation> Relations { get; set; } = new List<WorkItemRelation> ();
    }


}

