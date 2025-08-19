using Lamdat.ADOAutomationTool.Entities;
using Moq;
using System.Collections.Concurrent;

namespace Lamdat.ADOAutomationTool.Tests.Framework
{
    /// <summary>
    /// Mock implementation of IAzureDevOpsClient for testing scheduled scripts
    /// </summary>
    public class MockAzureDevOpsClient : IAzureDevOpsClient
    {
        private readonly ConcurrentDictionary<int, WorkItem> _workItems = new();
        private readonly List<WorkItemRelation> _workItemRelations = new();
        private readonly List<IterationDetails> _iterations = new();
        private int _nextWorkItemId = 1000;

        public string Project { get; set; } = "TestProject";

        // Track operations for verification
        public List<WorkItem> SavedWorkItems { get; } = new();
        public List<(WorkItem workItem, List<WorkItemRelation> relations)> SavedRelations { get; } = new();
        public List<QueryLinksByWiqlPrms> ExecutedQueries { get; } = new();

        public Task<List<IterationDetails>> GetAllTeamIterations(string teamName)
        {
            return Task.FromResult(_iterations.Where(i => i.TeamName == teamName).ToList());
        }

        public Task<IterationDetails> GetTeamsIterationDetailsByName(string teamName, string iterationName)
        {
            var iteration = _iterations.FirstOrDefault(i => i.TeamName == teamName && i.Name == iterationName);
            return Task.FromResult(iteration);
        }

        public Task<WorkItem> GetWorkItem(int workItemId)
        {
            _workItems.TryGetValue(workItemId, out var workItem);
            return Task.FromResult(workItem);
        }

        public Task<List<WorkItem>> QueryLinksByWiql(QueryLinksByWiqlPrms queryLinksByWiqlPrms)
        {
            ExecutedQueries.Add(queryLinksByWiqlPrms);
            
            // Simple mock implementation - return matching work items based on query
            var results = new List<WorkItem>();
            if (!string.IsNullOrEmpty(queryLinksByWiqlPrms.Wiql))
            {
                var wiql = queryLinksByWiqlPrms.Wiql;
                
                // Handle WorkItemLinks queries
                if (wiql.Contains("FROM WorkItemLinks"))
                {
                    results.AddRange(HandleWorkItemLinksQuery(wiql));
                }
                // Handle simple WorkItems queries
                else if (wiql.Contains("System.WorkItemType"))
                {
                    // Extract work item type from WIQL query
                    var workItemTypes = new[] { "Bug", "Task", "User Story", "Product Backlog Item", "Feature", "Epic" };
                    
                    foreach (var type in workItemTypes)
                    {
                        if (wiql.Contains($"'{type}'") || wiql.Contains($"\"{type}\""))
                        {
                            results.AddRange(_workItems.Values.Where(w => w.WorkItemType == type));
                        }
                    }
                }
                else
                {
                    // Return all work items for general queries
                    results.AddRange(_workItems.Values);
                }
            }
            else
            {
                // Return linked work items based on source ID and link type
                var sourceWorkItem = _workItems.Values.FirstOrDefault(w => w.Id == queryLinksByWiqlPrms.SourceWorkItemId);
                if (sourceWorkItem != null)
                {
                    var linkedIds = sourceWorkItem.Relations
                        .Where(r => string.IsNullOrEmpty(queryLinksByWiqlPrms.LinkType) || r.RelationType == queryLinksByWiqlPrms.LinkType)
                        .Select(r => r.RelatedWorkItemId)
                        .ToList();
                    
                    results.AddRange(_workItems.Values.Where(w => linkedIds.Contains(w.Id)));
                }
            }
            
            return Task.FromResult(results);
        }

        private List<WorkItem> HandleWorkItemLinksQuery(string wiql)
        {
            var results = new List<WorkItem>();
            
            // Parse the WorkItemLinks query to extract key information
            var isHierarchyReverse = wiql.Contains("Hierarchy-Reverse");
            var isHierarchyForward = wiql.Contains("Hierarchy-Forward");
            
            // Extract work item ID(s) from WHERE clause
            var workItemIds = ExtractWorkItemIdsFromQuery(wiql);
            if (workItemIds.Count == 0) return results;
            
            if (isHierarchyReverse)
            {
                // Find parents: WHERE [Source].[System.Id] IN (childIds)
                // Look for work items that have these IDs as children
                foreach (var workItemId in workItemIds)
                {
                    foreach (var kvp in _workItems)
                    {
                        var workItem = kvp.Value;
                        var hasChildRelation = workItem.Relations.Any(r => 
                            r.RelationType == "Child" && r.RelatedWorkItemId == workItemId);
                        
                        if (hasChildRelation)
                        {
                            // Create a result work item with the fields expected by the query
                            var resultItem = CreateQueryResultWorkItem(workItem, wiql);
                            if (resultItem != null && !results.Any(r => r.Id == resultItem.Id))
                            {
                                results.Add(resultItem);
                            }
                        }
                    }
                }
            }
            else if (isHierarchyForward)
            {
                // Find children: WHERE [Source].[System.Id] IN (parentIds)
                // Look for work items that are children of these parents
                foreach (var workItemId in workItemIds)
                {
                    var parentWorkItem = _workItems.Values.FirstOrDefault(w => w.Id == workItemId);
                    if (parentWorkItem != null)
                    {
                        foreach (var relation in parentWorkItem.Relations.Where(r => r.RelationType == "Child"))
                        {
                            var childWorkItem = _workItems.Values.FirstOrDefault(w => w.Id == relation.RelatedWorkItemId);
                            if (childWorkItem != null && !results.Any(r => r.Id == childWorkItem.Id))
                            {
                                var resultItem = CreateQueryResultWorkItem(childWorkItem, wiql);
                                if (resultItem != null && !results.Any(r => r.Id == resultItem.Id))
                                {
                                    results.Add(resultItem);
                                }
                            }
                        }
                    }
                }
            }
            
            return results;
        }

        private List<int> ExtractWorkItemIdsFromQuery(string wiql)
        {
            var ids = new List<int>();
            
            // Extract IDs from patterns like [Target].[System.Id] = 1234, [Source].[System.Id] = 1234, or IN clauses
            var patterns = new[]
            {
                @"\[Target\]\.\[System\.Id\]\s*=\s*(\d+)",
                @"\[Source\]\.\[System\.Id\]\s*=\s*(\d+)",
                @"\[Target\]\.\[System\.Id\]\s*IN\s*\(([^)]+)\)",
                @"\[Source\]\.\[System\.Id\]\s*IN\s*\(([^)]+)\)"
            };
            
            foreach (var pattern in patterns)
            {
                var match = System.Text.RegularExpressions.Regex.Match(wiql, pattern);
                if (match.Success)
                {
                    if (pattern.Contains("IN"))
                    {
                        // Handle IN clause - extract all IDs
                        var idsString = match.Groups[1].Value;
                        var idStrings = idsString.Split(',').Select(s => s.Trim()).ToArray();
                        foreach (var idString in idStrings)
                        {
                            if (int.TryParse(idString, out var id))
                            {
                                ids.Add(id);
                            }
                        }
                    }
                    else if (int.TryParse(match.Groups[1].Value, out var id))
                    {
                        ids.Add(id);
                    }
                }
            }
            
            return ids;
        }

        private WorkItem? CreateQueryResultWorkItem(WorkItem sourceWorkItem, string wiql)
        {
            // Create a work item that matches the SELECT fields in the query
            var resultItem = new WorkItem
            {
                Id = sourceWorkItem.Id,
                Fields = new Dictionary<string, object?>(sourceWorkItem.Fields)
            };
            
            // Handle AS clauses for renamed fields
            if (wiql.Contains("ParentId"))
            {
                resultItem.Fields["ParentId"] = sourceWorkItem.Id;
            }
            if (wiql.Contains("ParentType"))
            {
                resultItem.Fields["ParentType"] = sourceWorkItem.WorkItemType;
            }
            if (wiql.Contains("EpicId"))
            {
                resultItem.Fields["EpicId"] = sourceWorkItem.Id;
            }
            if (wiql.Contains("FeatureId"))
            {
                resultItem.Fields["FeatureId"] = sourceWorkItem.Id;
            }
            if (wiql.Contains("TaskId"))
            {
                resultItem.Fields["TaskId"] = sourceWorkItem.Id;
            }
            if (wiql.Contains("CompletedWork"))
            {
                resultItem.Fields["CompletedWork"] = sourceWorkItem.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork");
            }
            if (wiql.Contains("Activity"))
            {
                resultItem.Fields["Activity"] = sourceWorkItem.GetField<string>("Microsoft.VSTS.Common.Activity");
            }
            
            // Filter by work item type if specified in the query
            if (wiql.Contains("AND [Target].[System.WorkItemType]") || wiql.Contains("AND [Source].[System.WorkItemType]"))
            {
                var workItemTypes = new[] { "Epic", "Feature", "Product Backlog Item", "Bug", "Task" };
                var matchesType = false;
                
                foreach (var type in workItemTypes)
                {
                    if (wiql.Contains($"'{type}'") || wiql.Contains($"\"{type}\""))
                    {
                        if (sourceWorkItem.WorkItemType == type)
                        {
                            matchesType = true;
                            break;
                        }
                    }
                }
                
                if (!matchesType)
                {
                    return null;
                }
            }
            
            return resultItem;
        }

        public Task<bool> SaveWorkItem(WorkItem newWorkItem, bool logErrorOtherwiseWarn = false)
        {
            SavedWorkItems.Add(newWorkItem);
            
            if (newWorkItem.Id == 0)
            {
                // New work item - assign ID
                newWorkItem.Id = _nextWorkItemId++;
            }
            
            _workItems.AddOrUpdate(newWorkItem.Id, newWorkItem, (key, existing) => newWorkItem);
            return Task.FromResult(true);
        }

        public Task<bool> SaveWorkItemRelations(WorkItem workitem, List<WorkItemRelation> relations)
        {
            SavedRelations.Add((workitem, relations));
            _workItemRelations.AddRange(relations);
            return Task.FromResult(true);
        }

        public Task<ADOUser> WhoAmI()
        {
            return Task.FromResult(new ADOUser
            {
                Identity = new Identity
                {
                    DisplayName = "Test User",
                    MailAddress = "testuser@test.com",
                    TeamFoundationId = Guid.NewGuid().ToString()
                }
            });
        }

        // Helper methods for test setup
        public WorkItem CreateTestWorkItem(string workItemType = "Task", string title = "Test Work Item", string state = "New")
        {
            var workItem = new WorkItem
            {
                Id = _nextWorkItemId++,
                Fields = new Dictionary<string, object?>
                {
                    ["System.Title"] = title,
                    ["System.WorkItemType"] = workItemType,
                    ["System.State"] = state,
                    ["System.TeamProject"] = Project
                }
            };
            
            _workItems.TryAdd(workItem.Id, workItem);
            return workItem;
        }

        public void AddIteration(string teamName, string iterationName, DateTime startDate, DateTime endDate)
        {
            _iterations.Add(new IterationDetails
            {
                TeamName = teamName,
                Name = iterationName,
                StartDate = startDate,
                EndDate = endDate,
                Attributes = new IterationAttributes
                {
                    StartDate = startDate,
                    FinishDate = endDate
                }
            });
        }

        public void ClearAllData()
        {
            _workItems.Clear();
            _workItemRelations.Clear();
            _iterations.Clear();
            SavedWorkItems.Clear();
            SavedRelations.Clear();
            ExecutedQueries.Clear();
            _nextWorkItemId = 1000;
        }

        public List<WorkItem> GetAllWorkItems()
        {
            return _workItems.Values.ToList();
        }

        public Task<List<WorkItem>> QueryWorkItemsByWiql(string wiqlQuery, int? top = null)
        {
            if (string.IsNullOrWhiteSpace(wiqlQuery))
                return Task.FromResult(new List<WorkItem>());

            var results = new List<WorkItem>();
            
            // Handle simple WorkItems queries
            if (wiqlQuery.Contains("FROM WorkItems"))
            {
                // Start with all work items
                results.AddRange(_workItems.Values);
                
                // Handle work item type filtering - look for exact type matches in WHERE clause
                if (wiqlQuery.Contains("[System.WorkItemType] ="))
                {
                    var workItemTypes = new[] { "Bug", "Task", "User Story", "Product Backlog Item", "Feature", "Epic", "Glitch" };
                    
                    foreach (var type in workItemTypes)
                    {
                        if (wiqlQuery.Contains($"[System.WorkItemType] = '{type}'") || 
                            wiqlQuery.Contains($"[System.WorkItemType] = \"{type}\""))
                        {
                            results = results.Where(w => w.WorkItemType == type).ToList();
                            break; // Only one type per query
                        }
                    }
                }
                
                // Handle project filtering
                if (wiqlQuery.Contains("AND [System.TeamProject] = 'PCLabs'"))
                {
                    results = results.Where(w => w.GetField<string>("System.TeamProject") == "PCLabs").ToList();
                }
                
                // Handle date filtering - for testing, return items that have been recently updated
                if (wiqlQuery.Contains("[System.ChangedDate] >="))
                {
                    var today = DateTime.Now.Date;
                    results = results.Where(w => 
                    {
                        var changedDate = w.GetField<DateTime?>("System.ChangedDate");
                        return changedDate.HasValue && changedDate.Value.Date >= today;
                    }).ToList();
                }
                
                // Handle completed work filtering - filter by completed work > 0
                if (wiqlQuery.Contains("[Microsoft.VSTS.Scheduling.CompletedWork] > 0"))
                {
                    results = results.Where(w => 
                    {
                        var completedWork = w.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork");
                        return completedWork.HasValue && completedWork.Value > 0;
                    }).ToList();
                }
            }
            // Handle WorkItemLinks queries by delegating to existing logic
            else if (wiqlQuery.Contains("FROM WorkItemLinks"))
            {
                results.AddRange(HandleWorkItemLinksQuery(wiqlQuery));
            }
            else
            {
                // Return all work items for general queries
                results.AddRange(_workItems.Values);
            }
            
            // Apply top limit if specified
            if (top.HasValue && top > 0)
            {
                results = results.Take(top.Value).ToList();
            }
            
            return Task.FromResult(results);
        }
    }
}