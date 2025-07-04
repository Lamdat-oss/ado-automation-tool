﻿using Lamdat.ADOAutomationTool.Entities;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;


namespace Lamdat.ADOAutomationTool.Service
{
    public class AzureDevOpsClient : IAzureDevOpsClient
    {
        private readonly string _collectionURL;
        private readonly string _personalAccessToken;
        private readonly HttpClient _client;
        private readonly string _apiVersion = "6.0";
        private const int MAX_LINKED_ITEMS = 200;
        private readonly Serilog.ILogger _logger;
        private readonly bool _bypassRules;
        private static readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
        public string Project { get; set; }


        public AzureDevOpsClient(Serilog.ILogger logger, string organizationUrl, string personalAccessToken, bool bypassRules, bool notValidCerts)
        {
            List<string> errors = new List<string>();

            if (string.IsNullOrEmpty(organizationUrl))
                throw new ArgumentNullException(nameof(organizationUrl));

            if (string.IsNullOrEmpty(personalAccessToken))
                throw new ArgumentNullException(nameof(personalAccessToken));

            if (logger == null)
                throw new ArgumentNullException(nameof(logger));

            HttpClientHandler handler = new HttpClientHandler();
            if (notValidCerts == true)
            {
                handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
                };
            }


            _collectionURL = organizationUrl;
            _personalAccessToken = personalAccessToken;
            _bypassRules = bypassRules;
            _client = new HttpClient(handler);

            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(string.Format("{0}:{1}", "", personalAccessToken))));
            _client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=1");

            _logger = logger;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="workItemId"></param>
        /// <returns></returns>
        public async Task<WorkItem> GetWorkItem(int workItemId)
        {
            if (Project == "be9b3917-87e6-42a4-a549-2bc06a7a878f") // ADO Test 
                return new WorkItem() { Fields = new Dictionary<string, object>(), Id = 0 };

            try
            {
                var url = $"{_collectionURL}/{Project}/_apis/";

                var response = await _client.GetAsync($"{url}wit/workitems/{workItemId}?api-version={_apiVersion}&$expand=All");

                if (response.IsSuccessStatusCode)
                {
                    var jsonString = await response.Content.ReadAsStringAsync();
                    var workItem = JsonConvert.DeserializeObject<WorkItem>(jsonString);

                    if (workItem == null)
                    {
                        var errorMessage = $"Failed to retrieve a work item with ID '{workItemId}'. " +
                                           $"Unexpected API response format: {jsonString}";
                     
                        _logger.Error(errorMessage);
                        return new WorkItem() { Fields = new Dictionary<string, object>(), Id = 0 };
                    }

                    SetWorkItemRelationsAndSaveSystemConnection(workItem);
                    
                    return workItem;
                }
                else
                {
                    var errorMessage = "Failed to retrieve work item.";
                    if (response.Content != null)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        errorMessage += $" Error: {errorContent}";
                    }
                    _logger.Error(errorMessage);
                    return new WorkItem() { Fields = new Dictionary<string, object>(), Id = 0 };
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to retrieve work item, the error was : {ex.Message}");
                return new WorkItem() { Fields = new Dictionary<string, object>(), Id = 0 };
            }
        }

        public async Task<List<WorkItem>> QuetyLinksByWiql(QueryLinksByWiqlPrms queryLinksByWiqlPrms)
        {
            if (queryLinksByWiqlPrms == null)
                throw new ArgumentNullException(nameof(queryLinksByWiqlPrms));

            var totalLinkedWorkItems = new List<WorkItem>();
            
            try
            {
                var url = $"{_collectionURL}/{Project}/_apis/wit/wiql?";

                if (queryLinksByWiqlPrms.Top.HasValue &&
                    queryLinksByWiqlPrms.Top > 0)
                {
                    url += $"$top={queryLinksByWiqlPrms.Top}&";
                }
                
                url += $"api-version={_apiVersion}";
                
                var queryString = $@" SELECT [System.Id]
                    FROM workitemLinks
                    WHERE
                        (
                            [Source].[System.WorkItemType] = '{queryLinksByWiqlPrms.SourceWorkItemType}'
                            AND [Source].[System.Id] = '{queryLinksByWiqlPrms.SourceWorkItemId}'
                        ) ";

                if (!string.IsNullOrEmpty(queryLinksByWiqlPrms.LinkType))
                {
                    queryString += $@" 
                        AND (
                            [System.Links.LinkType] = '{queryLinksByWiqlPrms.LinkType}'
                        ) ";
                }

                if (!string.IsNullOrEmpty(queryLinksByWiqlPrms.TargetWorkItemType))
                {
                    queryString += $@"
                        AND (
                            [Target].[System.WorkItemType] = '{queryLinksByWiqlPrms.TargetWorkItemType}'
                        ) ";
                }
                
                queryString += $@" 
                    ORDER BY [System.ChangedDate] DESC
                    MODE (MustContain)";
                
                var wiql = new { query = queryString };
                
                
                var postValue = new StringContent(JsonConvert.SerializeObject(wiql), Encoding.UTF8, "application/json"); //mediaType needs to be application/json for a post call

                // send query to REST endpoint to return list of id's from query
                var method = new HttpMethod("POST");
                var httpRequestMessage = new HttpRequestMessage(method, url) { Content = postValue };
                var httpResponseMessage = await _client.SendAsync(httpRequestMessage);
                

                if (httpResponseMessage.IsSuccessStatusCode)
                {
                    var jsonString = await httpResponseMessage.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<WiqlQueryResult>(jsonString);

                    if (result == null)
                    {
                        var errorMessage = $"Failed to retrieve the work item's referenced items for " +
                                           $"'{queryLinksByWiqlPrms.SourceWorkItemType}' with ID '{queryLinksByWiqlPrms.SourceWorkItemId}'. " +
                                           $"Unexpected API response format: {jsonString}";
                     
                        _logger.Error(errorMessage);
                        return new List<WorkItem>();
                    }
                    else
                    {

                        var relevantRelations = result.WorkItemRelations
                            .Where(wiRel => !string.IsNullOrEmpty(wiRel.Rel) &&
                                            wiRel.Target != null && 
                                            wiRel.Target.ID > 0 &&
                                            wiRel.Source != null &&
                                            wiRel.Source.ID == queryLinksByWiqlPrms.SourceWorkItemId)
                            .ToList();
                        
                        

                        var skip = 0;
                        var total = relevantRelations.Count;
                        
                        while (skip < total)
                        {
                            var idsBuilder = new System.Text.StringBuilder();
                            var pageSize = 0;
                            
                            for (var i = 0; i < MAX_LINKED_ITEMS && skip + i < total; i++)
                            {
                                var relatedWorkItem = relevantRelations.ElementAtOrDefault(skip + i);
                                if (relatedWorkItem != null && relatedWorkItem.Target != null)
                                {
                                    idsBuilder.Append(relatedWorkItem.Target.ID.ToString()).Append(",");
                                    pageSize++;
                                }
                            }
                            string ids = idsBuilder.ToString().TrimEnd(new char[] { ',' });
                            
                            
                            if (!string.IsNullOrWhiteSpace(ids))
                            {
                                var idsUrl = $"{_collectionURL}/{Project}/_apis/wit/workitems?ids={ids}";
                                
                                if (queryLinksByWiqlPrms.Fields != null && queryLinksByWiqlPrms.Fields.Count > 0)
                                {
                                    var fieldsBuilder = new System.Text.StringBuilder();
                                    foreach (var field in queryLinksByWiqlPrms.Fields)
                                    {
                                        fieldsBuilder.Append(field).Append(",");
                                    }
                                    string fieldsStr = fieldsBuilder.ToString().TrimEnd(new char[] { ',' });
                                    
                                    idsUrl += $"&fields={fieldsStr}";
                                }
                                else
                                {
                                    idsUrl += $"&$expand=all";
                                }
                                
                                idsUrl += $"&api-version={_apiVersion}";
                                
                                
                                var getLinkedWorkItemsWithDetailsResponse = await _client.GetAsync(idsUrl);

                                if (getLinkedWorkItemsWithDetailsResponse.IsSuccessStatusCode)
                                {
                                    var dataResult = await getLinkedWorkItemsWithDetailsResponse.Content.ReadAsStringAsync();
                                    var resultWits = JsonConvert.DeserializeObject<QueryResult<WorkItem>>(dataResult);

                                    if (resultWits == null)
                                    {
                                        var errorMessage = $"Failed to retrieve the work item's referenced items with details for " +
                                                           $"'{queryLinksByWiqlPrms.SourceWorkItemType}' with ID '{queryLinksByWiqlPrms.SourceWorkItemId}'. " +
                                                           $"Unexpected API response format: {dataResult}";
                         
                                        _logger.Error(errorMessage);
                                    }
                                    else
                                    {
                                        foreach (WorkItem workItem in resultWits.Value)
                                        {
                                            SetWorkItemRelationsAndSaveSystemConnection(workItem);
                                            totalLinkedWorkItems.Add(workItem);
                                        }
                                    }
                                }
                                else
                                {
                                    var apiErrorMsg = await getLinkedWorkItemsWithDetailsResponse.Content.ReadAsStringAsync();
                                    var errorMessage = $"Failed to retrieve the work item's referenced items with details for " +
                                        $"'{queryLinksByWiqlPrms.SourceWorkItemType}' with ID '{queryLinksByWiqlPrms.SourceWorkItemId}': " +
                                        $"{apiErrorMsg}";
                                    
                                    _logger.Error(errorMessage);
                                }
                            }


                            if (skip + pageSize == skip)
                            {
                                break;
                            }
                            else
                            {
                                skip += pageSize;   
                            }
                        }

                        return totalLinkedWorkItems;
                    }
                    
                    return new List<WorkItem>();
                }
                else
                {
                    var errorMessage = $"Failed to retrieve the work item's referenced items for " +
                                       $"'{queryLinksByWiqlPrms.SourceWorkItemType}' with ID '{queryLinksByWiqlPrms.SourceWorkItemId}'.";
                    if (httpResponseMessage.Content != null)
                    {
                        var errorContent = await httpResponseMessage.Content.ReadAsStringAsync();
                        errorMessage += $" Error: {errorContent}";
                    }
                    _logger.Error(errorMessage);
                    return new List<WorkItem>();
                }
            }
            catch (Exception ex)
            {
                var errorMessage = $"An error has been occur during API call to fetch the work item's referenced items for " +
                                   $"'{queryLinksByWiqlPrms.SourceWorkItemType}' with ID '{queryLinksByWiqlPrms.SourceWorkItemId}'.";
                _logger.Error($"{errorMessage} {ex.Message}");
                return new List<WorkItem>();
            }
        }

        public async Task<ADOUser> WhoAmI()
        {
            try
            {
                var url = $"{_collectionURL}/_api/_common/GetUserProfile";

                var response = await _client.GetAsync($"{url}?api-version={_apiVersion}");

                if (response.IsSuccessStatusCode)
                {
                    var jsonString = await response.Content.ReadAsStringAsync();
                    var user = JsonConvert.DeserializeObject<ADOUser>(jsonString);

                    return user;
                }
                else
                {
                    var errorMessage = $"Error getting current user. {response.StatusCode} ";
                    if (response.Content != null)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        errorMessage += $" Error: {errorContent}";
                    }
                    _logger.Error(errorMessage);
                    throw new ADOAutomationException(errorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex.Message);
                if (ex.InnerException != null)
                    _logger.Error(ex.InnerException.Message);

                throw new ADOAutomationException($"Failed to retreive current user, the error was : {ex.Message}");

            }
        }

        /// <summary>
        /// Saves work item
        /// </summary>
        /// <param name="workItem"></param>
        /// <returns></returns>
        public async Task<bool> SaveWorkItem(WorkItem newWorkItem, bool logErrorOtherwiseWarn = false)
        {
            if (newWorkItem == null)
                throw new ArgumentNullException(nameof(newWorkItem));

            if (Project == "be9b3917-87e6-42a4-a549-2bc06a7a878f") // ADO Test 
                return true;

            try
            {

                var existingWorkItem = await GetWorkItem(newWorkItem.Id);

                if (existingWorkItem == null || existingWorkItem.Id == 0)
                {
                    _logger.Error($"Work item with ID {newWorkItem.Id} not found.");
                    throw new ADOAutomationException($"Work item with ID {newWorkItem.Id} not found.");
                }

                var patchOperations = new List<JsonPatchOperation>();

                foreach (var kvp in newWorkItem.Fields)
                {
                    var fieldName = kvp.Key;
                    var newValue = kvp.Value;

                    if (existingWorkItem.Fields.ContainsKey(fieldName))
                    {
                        if (fieldName == null || fieldName == "System.ChangedDate" || fieldName == "System.CreatedBy" || fieldName == "System.ChangedBy" || fieldName == "System.AuthorizedAs")
                            continue;

                        var oldValue = existingWorkItem.Fields[fieldName];
                        if (!Equals(oldValue, newValue))
                        {
                            patchOperations.Add(new JsonPatchOperation
                            {
                                Operation = "replace",
                                Path = $"/fields/{fieldName}",
                                Value = newValue
                            });
                        }
                    }
                    else
                    {
                        patchOperations.Add(new JsonPatchOperation
                        {
                            Operation = "add",
                            Path = $"/fields/{fieldName}",
                            Value = newValue
                        });
                    }
                }

                if (patchOperations.Count == 0)
                    return true;

                var json = JsonConvert.SerializeObject(patchOperations);
                var content = new StringContent(json, Encoding.UTF8, "application/json-patch+json");

                var url = $"{_collectionURL}/{Project}/_apis/";
                var response = await _client.PatchAsync($"{url}/wit/workitems/{newWorkItem.Id}?api-version={_apiVersion}&bypassRules={_bypassRules}", content);

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
                else
                {
                    var errorMessage = $"Failed to save work item with ID {newWorkItem.Id}.";
                    if (response.Content != null)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        errorMessage += $" Error: {errorContent}";
                    }
                    if (logErrorOtherwiseWarn)
                        _logger.Error(errorMessage);
                    else
                        _logger.Warning(errorMessage);
                    throw new ADOAutomationException(errorMessage);
                }
            }
            catch (Exception ex)
            {
                if (logErrorOtherwiseWarn)
                    _logger.Error($"An error occurred: {ex.Message}");
                else
                    _logger.Warning($"An error occurred: {ex.Message}");
                throw new ADOAutomationException($"Failed to save work item: {ex.Message}");
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="workItemId"></param>
        /// <param name="relations"></param>
        /// <returns></returns>
        public async Task<bool> SaveWorkItemRelations(WorkItem workitem, List<WorkItemRelation> relations)
        {

            if (workitem == null)
                throw new ArgumentNullException(nameof(workitem));

            if (relations == null)
                return true;

            try
            {
                var url = $"{_collectionURL}/{workitem.Project}/_apis/";

                var payload = new
                {
                    relations
                };

                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _client.PatchAsync($"{url}wit/workitems/{workitem.Id}?api-version={_apiVersion}", content);

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
                else
                {
                    var errorMessage = "Failed saving work item relations.";
                    if (response.Content != null)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        errorMessage += $" Error: {errorContent}";
                    }
                    _logger.Error(errorMessage);
                    throw new ADOAutomationException(errorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex.Message);
                throw new ADOAutomationException($"Failed saving work item relations, the error was : {ex.Message}");
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="teamName"></param>
        /// <returns></returns>
        public async Task<List<IterationDetails>> GetAllTeamIterations(string teamName)
        {
            var cacheKey = $"AllIterations_{teamName}";
            var cacheEntryOptions = new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromMinutes(2));

            if (_cache.TryGetValue(cacheKey, out List<IterationDetails> iterations))
            {
                return iterations;
            }
            else
            {
                iterations = await GetIterationsFromApi(teamName);
                _cache.Set(cacheKey, iterations, cacheEntryOptions);
                return iterations;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="teamName"></param>
        /// <returns></returns>
        private async Task<List<IterationDetails>> GetIterationsFromApi(string teamName)
        {
            var allIterations = new List<IterationDetails>();

            try
            {
                var url = $"{_collectionURL}/{Project}/{teamName}/_apis/work/teamsettings/iterations";
                var response = await _client.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var jsonString = await response.Content.ReadAsStringAsync();
                    var iterationResponse = JsonConvert.DeserializeObject<IterationDetailsResponse>(jsonString);

                    allIterations.AddRange(iterationResponse.Value);

                    return allIterations;
                }
                else
                {
                    var errorMessage = $"Failed to retrieve iteration details. {response.StatusCode}";
                    if (response.Content != null)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        errorMessage += $" Error: {errorContent}";
                    }
                    _logger.Error(errorMessage);
                    throw new ADOAutomationException(errorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex.Message);
                throw new ADOAutomationException($"Failed to retrieve iteration details, the error was : {ex.Message}");
            }
        }


        /// <summary>
        /// Get IterationDetails By Name
        /// </summary>
        /// <param name="iterationName"></param>
        /// <param name="teamName"></param>
        /// <returns></returns>
        /// <exception cref="ADOAutomationException"></exception>
        public async Task<IterationDetails> GetTeamsIterationDetailsByName(string teamName, string iterationName)
        {
            if (string.IsNullOrWhiteSpace(iterationName))
                throw new ArgumentNullException(nameof(iterationName));

            if (string.IsNullOrWhiteSpace(teamName))
                throw new ArgumentNullException(nameof(teamName));

            try
            {
                var iterations = await GetAllTeamIterations(teamName);
                if (iterations != null)
                {

                    var iteration = iterations?.FirstOrDefault(i => i.Path == iterationName);
                    return iteration;
                }
                else
                {
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex.Message);
                throw new ADOAutomationException($"Failed to retrieve iteration details, the error was : {ex.Message}");
            }
        }

        private void SetWorkItemRelationsAndSaveSystemConnection(WorkItem workItem)
        {
            if (workItem.relations != null)
            {
                // https://learn.microsoft.com/en-us/azure/devops/boards/queries/link-type-reference?view=azure-devops

                foreach (var relation in workItem.relations)
                {
                    string? relationType = relation.attributes?.name?.ToString();
                    string relationName = relation.rel.ToString();

                    if (relationName.StartsWith("System.") || relationName.StartsWith("Microsoft."))
                    {
                        string relUrl = relation.url.ToString();
                        var lastIdx = relUrl.LastIndexOf('/');
                        var relatedWorkItemIdStr = relUrl.Substring(lastIdx + 1, relUrl.Length - lastIdx - 1);
                        var relatedWorkItemId = int.Parse(relatedWorkItemIdStr);


                        workItem.Relations.Add(new WorkItemRelation
                        {
                            Rel = relation.rel,
                            Url = relation.url,
                            RelationType = relationType,
                            RelatedWorkItemId = relatedWorkItemId
                        });
                    }
                }
            }
                    
            workItem.Parent = workItem?.Relations?.Where(c => c?.RelationType == "Parent").FirstOrDefault();
            workItem.Children = workItem?.Relations?.Where(c => c?.RelationType == "Child").ToList();
                    
            workItem.SetClient(this, _logger);
        }
        
        
        ///// <summary>
        ///// 
        ///// </summary>
        ///// <param name="workItemId"></param>
        ///// <returns></returns>
        ///// <exception cref="ArgumentNullException"></exception>
        ///// <exception cref="ADOAutomationException"></exception>
        //public async Task<ADOUser> GetLastChangedByUserForWorkItem(int workItemId)
        //{
        //    try
        //    {
        //        var revisions = new List<Revision>();
        //        var skip = 0;
        //        var pageSize = 100;

        //        while (true)
        //        {
        //            var url = $"{_collectionURL}/{Project}/_apis/wit/workitems/{workItemId}/revisions?$top={pageSize}&$skip={skip}&api-version={_apiVersion}";
        //            var response = await _client.GetAsync(url);

        //            if (response.IsSuccessStatusCode)
        //            {
        //                var jsonString = await response.Content.ReadAsStringAsync();
        //                var revisionResponse = JsonConvert.DeserializeObject<RevisionResponse>(jsonString);
        //                if (revisionResponse.Value == null || !revisionResponse.Value.Any() || revisionResponse.Value.Count < pageSize)
        //                {
        //                    if (revisionResponse.Value != null && revisionResponse.Value.Count < pageSize)
        //                        revisions.AddRange(revisionResponse.Value);

        //                    break;
        //                }

        //                skip += pageSize;
        //            }
        //            else
        //            {
        //                var errorMessage = $"Failed to retrieve revisions for work item {workItemId}.";
        //                if (response.Content != null)
        //                {
        //                    var errorContent = await response.Content.ReadAsStringAsync();
        //                    errorMessage += $" Error: {errorContent}";
        //                }
        //                _logger.Error(errorMessage);
        //                throw new ADOAutomationException(errorMessage);
        //            }
        //        }

        //        var lastRevision = revisions.OrderByDescending(r => r.Fields.SystemChangedDate).FirstOrDefault();
        //        if (lastRevision != null)
        //        {
        //            return new ADOUser
        //            {
        //                Identity = new Identity
        //                {
        //                    DisplayName = lastRevision.Fields.SystemChangedBy.DisplayName,
        //                    EntityId = lastRevision.Fields.SystemChangedBy.Id,
        //                    SubHeader = lastRevision.Fields.SystemChangedBy.UniqueName
        //                }
        //            };
        //        }
        //        else
        //        {
        //            _logger.Debug($"No revisions found for work item {workItemId}");
        //            return null;
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.Debug($"Failed to retrieve revisions for work item {workItemId}, the error was : {ex.Message}");
        //        throw new ADOAutomationException($"Failed to retrieve revisions for work item {workItemId}, the error was : {ex.Message}");
        //    }
        //}
    }
}


