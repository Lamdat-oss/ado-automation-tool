using Newtonsoft.Json;

namespace Lamdat.ADOAutomationTool.Entities
{
    public class WorkItem
    {
        /**
         * Private properties 
         */
        // public IAzureDevOpsClient? Client { get; set; }
        private IAzureDevOpsClient? _client { get; set; }
        private Serilog.ILogger _logger  { get; set; }
        
        /**
         * Public properties
         */
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("rev")]
        public int Revision { get; set; }

        [JsonProperty("fields")]
        public Dictionary<string, object?> Fields { get; set; } = new Dictionary<string, object?>();

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

        public WorkItemRelation? Parent { get; set; }

        public List<WorkItemRelation> Children { get; set; } = new List<WorkItemRelation>();

        [JsonProperty("relations")]
        public dynamic relations { get; private set; }


        public List<WorkItemRelation> Relations { get; set; } = new List<WorkItemRelation> ();
        
        
        /**
         * Methods
         */
        
        
        public void SetClient(IAzureDevOpsClient client, Serilog.ILogger logger)
        {
            _client = client;
            _logger = logger;
        }

        public void SetField(string fieldName, object? value)
        {
            if (Fields.ContainsKey(fieldName))
                Fields[fieldName] = value;
            else
                Fields.Add(fieldName, value);
        }

        public T? GetField<T>(string fieldName, T defaultValue)
        {
            if (string.IsNullOrEmpty(fieldName))
                throw new ArgumentNullException(nameof(fieldName));

            if (Fields.TryGetValue(fieldName, out var fieldValue))
            {
                return (T)fieldValue;
            }
            
            return defaultValue;
        }
        
        
        public T? GetField<T>(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName))
                throw new ArgumentNullException(nameof(fieldName));

            if (Fields.TryGetValue(fieldName, out var fieldValue))
            {
                return (T)fieldValue;
            }
            
            return default(T);
        }
        
        
        public async Task<List<WorkItem>> FollowingLinks(FollowingLinksPrms followingLinksPrms)
        {
            if (followingLinksPrms == null)
                throw new ArgumentNullException(nameof(followingLinksPrms));
            
            if (_client == null)
                throw new ArgumentNullException(nameof(_client));
            
            try
            {
                var	linkedWorkItems = await _client.QuetyLinksByWiql(new QueryLinksByWiqlPrms
                {
                    SourceWorkItemId = Id,
                    SourceWorkItemType = WorkItemType,
                    LinkType = followingLinksPrms.LinkType,
                    TargetWorkItemType = followingLinksPrms.WhereTypeIs,
                    Top = followingLinksPrms.AtMost
                });

                return linkedWorkItems;
            }
            catch (Exception ex)
            {
                var errorMessage = $"An error has been occur during get of linked work items for " +
                                   $"'{WorkItemType}' with ID '{Id}'.";
                _logger.Error($"{errorMessage} {ex.Message}");
                return new List<WorkItem>();
            }
        }
        
    }


}

