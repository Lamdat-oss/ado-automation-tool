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

        public T? GetField<T>(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName))
                throw new ArgumentNullException(nameof(fieldName));

            if (Fields.TryGetValue(fieldName, out var fieldValue))
            {
                return ConvertFieldValue<T>(fieldValue);
            }
            
            return default(T);
        }

        public T? GetField<T>(string fieldName, T defaultValue)
        {
            if (string.IsNullOrEmpty(fieldName))
                throw new ArgumentNullException(nameof(fieldName));

            if (Fields.TryGetValue(fieldName, out var fieldValue))
            {
                return ConvertFieldValue<T>(fieldValue);
            }
            
            return defaultValue;
        }

        /// <summary>
        /// Converts field values to the requested type with intelligent type conversion.
        /// Handles common Azure DevOps scenarios where numeric values are returned as strings.
        /// </summary>
        private T? ConvertFieldValue<T>(object? fieldValue)
        {
            if (fieldValue == null)
                return default(T);

            var targetType = typeof(T);
            var sourceType = fieldValue.GetType();

            // If types match exactly, return as-is
            if (sourceType == targetType)
                return (T)fieldValue;

            // Handle nullable types
            var underlyingType = Nullable.GetUnderlyingType(targetType);
            if (underlyingType != null)
            {
                // If it's a nullable type, work with the underlying type
                targetType = underlyingType;
            }

            try
            {
                // Handle string to numeric conversions (common in Azure DevOps JSON)
                if (sourceType == typeof(string) && fieldValue is string stringValue)
                {
                    if (string.IsNullOrWhiteSpace(stringValue))
                        return default(T);

                    // Handle common numeric types
                    if (targetType == typeof(double))
                        return (T)(object)double.Parse(stringValue);
                    if (targetType == typeof(float))
                        return (T)(object)float.Parse(stringValue);
                    if (targetType == typeof(int))
                        return (T)(object)int.Parse(stringValue);
                    if (targetType == typeof(long))
                        return (T)(object)long.Parse(stringValue);
                    if (targetType == typeof(decimal))
                        return (T)(object)decimal.Parse(stringValue);
                    if (targetType == typeof(bool))
                        return (T)(object)bool.Parse(stringValue);
                    if (targetType == typeof(DateTime))
                        return (T)(object)DateTime.Parse(stringValue);
                }

                // Handle numeric to string conversions
                if (targetType == typeof(string))
                    return (T)(object)fieldValue.ToString();

                // Handle numeric type conversions (e.g., int to double)
                if (IsNumericType(sourceType) && IsNumericType(targetType))
                    return (T)Convert.ChangeType(fieldValue, targetType);

                // Try direct conversion for other types
                return (T)Convert.ChangeType(fieldValue, targetType);
            }
            catch (Exception)
            {
                // If conversion fails, try direct cast as last resort
                try
                {
                    return (T)fieldValue;
                }
                catch (InvalidCastException)
                {
                    // If all else fails, return default
                    return default(T);
                }
            }
        }

        /// <summary>
        /// Determines if a type is a numeric type
        /// </summary>
        private static bool IsNumericType(Type type)
        {
            return type == typeof(byte) || type == typeof(sbyte) ||
                   type == typeof(short) || type == typeof(ushort) ||
                   type == typeof(int) || type == typeof(uint) ||
                   type == typeof(long) || type == typeof(ulong) ||
                   type == typeof(float) || type == typeof(double) ||
                   type == typeof(decimal);
        }
        
        
        public async Task<List<WorkItem>> FollowingLinks(FollowingLinksPrms followingLinksPrms)
        {
            if (followingLinksPrms == null)
                throw new ArgumentNullException(nameof(followingLinksPrms));
            
            if (_client == null)
                throw new ArgumentNullException(nameof(_client));
            
            try
            {
                var	linkedWorkItems = await _client.QueryLinksByWiql(new QueryLinksByWiqlPrms
                {
                    SourceWorkItemId = Id,
                    SourceWorkItemType = WorkItemType,
                    LinkType = followingLinksPrms.LinkType,
                    TargetWorkItemType = followingLinksPrms.WhereTypeIs,
                    Top = followingLinksPrms.AtMost,
                    Fields = followingLinksPrms.FieldsToQuery
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

