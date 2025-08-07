using Lamdat.ADOAutomationTool.Tests.Framework;
using Lamdat.ADOAutomationTool.Entities;

namespace Lamdat.ADOAutomationTool.Tests.ScheduledScripts
{
    /// <summary>
    /// Base class for scheduled script tests that provides common setup and utilities
    /// </summary>
    public abstract class ScheduledScriptTestBase : IDisposable
    {
        protected readonly ScheduledScriptTestRunner TestRunner;
        protected MockAzureDevOpsClient MockClient => TestRunner.MockClient;

        protected ScheduledScriptTestBase()
        {
            TestRunner = new ScheduledScriptTestRunner();
        }

        /// <summary>
        /// Execute a script and return the result
        /// </summary>
        protected async Task<ScheduledScriptTestResult> ExecuteScriptAsync(string scriptCode, CancellationToken cancellationToken = default)
        {
            return await TestRunner.ExecuteScriptAsync(scriptCode, cancellationToken);
        }

        /// <summary>
        /// Execute a script from a file
        /// </summary>
        protected async Task<ScheduledScriptTestResult> ExecuteScriptFromFileAsync(string scriptFilePath, CancellationToken cancellationToken = default)
        {
            return await TestRunner.ExecuteScriptFromFileAsync(scriptFilePath, cancellationToken);
        }

        /// <summary>
        /// Create a test work item with default values
        /// </summary>
        protected WorkItem CreateTestWorkItem(string workItemType = "Task", string title = "Test Work Item", string state = "New")
        {
            return TestRunner.CreateTestWorkItem(workItemType, title, state);
        }

        /// <summary>
        /// Create multiple test work items
        /// </summary>
        protected List<WorkItem> CreateTestWorkItems(int count, string workItemType = "Task", string titlePrefix = "Test Item")
        {
            var workItems = new List<WorkItem>();
            for (int i = 1; i <= count; i++)
            {
                workItems.Add(CreateTestWorkItem(workItemType, $"{titlePrefix} {i}", "New"));
            }
            return workItems;
        }

        /// <summary>
        /// Add a test iteration
        /// </summary>
        protected void AddTestIteration(string teamName, string iterationName, DateTime startDate, DateTime endDate)
        {
            TestRunner.AddTestIteration(teamName, iterationName, startDate, endDate);
        }

        /// <summary>
        /// Add current sprint iteration (running now)
        /// </summary>
        protected void AddCurrentSprint(string teamName, string sprintName = "Current Sprint")
        {
            var now = DateTime.Now;
            AddTestIteration(teamName, sprintName, now.AddDays(-7), now.AddDays(7));
        }

        /// <summary>
        /// Add future sprint iteration
        /// </summary>
        protected void AddFutureSprint(string teamName, string sprintName = "Future Sprint")
        {
            var now = DateTime.Now;
            AddTestIteration(teamName, sprintName, now.AddDays(14), now.AddDays(28));
        }

        /// <summary>
        /// Clear all test data
        /// </summary>
        protected void ClearTestData()
        {
            TestRunner.ClearTestData();
        }

        /// <summary>
        /// Get all work items created during the test
        /// </summary>
        protected List<WorkItem> GetAllWorkItems()
        {
            return MockClient.GetAllWorkItems();
        }

        /// <summary>
        /// Helper to create a script that logs a message
        /// </summary>
        protected string CreateLoggingScript(string message)
        {
            return $@"Logger.Information(""{message}"");";
        }

        /// <summary>
        /// Helper to create a script that creates a work item
        /// </summary>
        protected string CreateWorkItemCreationScript(string title, string workItemType = "Task", string state = "New")
        {
            return $@"
                var newWorkItem = new WorkItem
                {{
                    Fields = new Dictionary<string, object?>
                    {{
                        [""System.Title""] = ""{title}"",
                        [""System.WorkItemType""] = ""{workItemType}"",
                        [""System.State""] = ""{state}"",
                        [""System.TeamProject""] = Client.Project
                    }}
                }};
                
                await Client.SaveWorkItem(newWorkItem);
                Logger.Information($""Created work item {{newWorkItem.Id}}: {title}"");
            ";
        }

        /// <summary>
        /// Helper to create a script that queries work items by type
        /// </summary>
        protected string CreateWorkItemQueryScript(string workItemType)
        {
            return $@"
                var queryParams = new QueryLinksByWiqlPrms
                {{
                    Wiql = ""SELECT [System.Id] FROM WorkItems WHERE [System.WorkItemType] = '{workItemType}'""
                }};
                
                var workItems = await Client.QuetyLinksByWiql(queryParams);
                Logger.Information($""Found {{workItems.Count}} {workItemType} items"");
                
                foreach (var item in workItems)
                {{
                    Logger.Information($""{workItemType}: {{item.Id}} - {{item.GetField<string>(""""System.Title"""")}}"");
                }}
            ";
        }

        /// <summary>
        /// Helper to create a script that updates a work item
        /// </summary>
        protected string CreateWorkItemUpdateScript(int workItemId, string newTitle, string newState = null)
        {
            var stateUpdate = newState != null ? $@"workItem.SetField(""System.State"", ""{newState}"");" : "";
            
            return $@"
                var workItem = await Client.GetWorkItem({workItemId});
                if (workItem != null)
                {{
                    workItem.SetField(""System.Title"", ""{newTitle}"");
                    {stateUpdate}
                    await Client.SaveWorkItem(workItem);
                    Logger.Information($""Updated work item {{workItem.Id}}"");
                }}
                else
                {{
                    Logger.Warning(""Work item {workItemId} not found"");
                }}
            ";
        }

        public virtual void Dispose()
        {
            TestRunner?.Dispose();
        }
    }
}