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

        public Task<List<WorkItem>> QuetyLinksByWiql(QueryLinksByWiqlPrms queryLinksByWiqlPrms)
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
            
            // Extract work item ID from WHERE clause
            var workItemId = ExtractWorkItemIdFromQuery(wiql);
            if (workItemId == null) return results;
            
            if (isHierarchyReverse)
            {
                // Find parents: WHERE [Target].[System.Id] = {childId}
                // Look for work items that have this ID as a child
                foreach (var kvp in _workItems)
                {
                    var workItem = kvp.Value;
                    var hasChildRelation = workItem.Relations.Any(r => 
                        r.RelationType == "Child" && r.RelatedWorkItemId == workItemId);
                    
                    if (hasChildRelation)
                    {
                        // Create a result work item with the fields expected by the query
                        var resultItem = CreateQueryResultWorkItem(workItem, wiql);
                        if (resultItem != null)
                        {
                            results.Add(resultItem);
                        }
                    }
                }
            }
            else if (isHierarchyForward)
            {
                // Find children: WHERE [Source].[System.Id] = {parentId}
                // Look for work items that are children of this parent
                var parentWorkItem = _workItems.Values.FirstOrDefault(w => w.Id == workItemId);
                if (parentWorkItem != null)
                {
                    foreach (var relation in parentWorkItem.Relations.Where(r => r.RelationType == "Child"))
                    {
                        var childWorkItem = _workItems.Values.FirstOrDefault(w => w.Id == relation.RelatedWorkItemId);
                        if (childWorkItem != null)
                        {
                            var resultItem = CreateQueryResultWorkItem(childWorkItem, wiql);
                            if (resultItem != null)
                            {
                                results.Add(resultItem);
                            }
                        }
                    }
                }
            }
            
            return results;
        }

        private int? ExtractWorkItemIdFromQuery(string wiql)
        {
            // Extract ID from patterns like [Target].[System.Id] = 1234 or [Source].[System.Id] = 1234
            var patterns = new[]
            {
                @"\[Target\]\.\[System\.Id\]\s*=\s*(\d+)",
                @"\[Source\]\.\[System\.Id\]\s*=\s*(\d+)"
            };
            
            foreach (var pattern in patterns)
            {
                var match = System.Text.RegularExpressions.Regex.Match(wiql, pattern);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var id))
                {
                    return id;
                }
            }
            
            return null;
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
    }
}