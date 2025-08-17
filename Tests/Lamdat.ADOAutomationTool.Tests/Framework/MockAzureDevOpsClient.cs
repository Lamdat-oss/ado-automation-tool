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
                // Simple WIQL parsing for testing
                if (queryLinksByWiqlPrms.Wiql.Contains("System.WorkItemType"))
                {
                    // Extract work item type from WIQL query
                    var wiql = queryLinksByWiqlPrms.Wiql;
                    var workItemTypes = new[] { "Bug", "Task", "User Story", "Feature" };
                    
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