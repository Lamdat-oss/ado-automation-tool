using Lamdat.ADOAutomationTool.Tests.Framework;
using Lamdat.ADOAutomationTool.Entities;

namespace Lamdat.ADOAutomationTool.Tests.Scripts
{
    /// <summary>
    /// Base class for webhook/context-based script tests (IScript) that provides common setup and utilities
    /// </summary>
    public abstract class ScriptTestBase : IDisposable
    {
        protected readonly ScriptTestRunner TestRunner;
        protected MockAzureDevOpsClient MockClient => TestRunner.MockClient;

        protected ScriptTestBase()
        {
            TestRunner = new ScriptTestRunner();
        }

        /// <summary>
        /// Execute a script with full context control
        /// </summary>
        protected async Task<ScriptTestResult> ExecuteScriptAsync(string scriptCode, IContext context, CancellationToken cancellationToken = default)
        {
            return await TestRunner.ExecuteScriptAsync(scriptCode, context, cancellationToken);
        }

        /// <summary>
        /// Execute a script with simplified parameters
        /// </summary>
        protected async Task<ScriptTestResult> ExecuteScriptAsync(
            string scriptCode, 
            WorkItem workItem = null,
            string eventType = "workitem.updated",
            string project = null,
            CancellationToken cancellationToken = default)
        {
            return await TestRunner.ExecuteScriptAsync(scriptCode, workItem, eventType, project, cancellationToken);
        }

        /// <summary>
        /// Execute a script from a file
        /// </summary>
        protected async Task<ScriptTestResult> ExecuteScriptFromFileAsync(string scriptFilePath, IContext context, CancellationToken cancellationToken = default)
        {
            return await TestRunner.ExecuteScriptFromFileAsync(scriptFilePath, context, cancellationToken);
        }

        /// <summary>
        /// Execute a script from a file with simplified parameters
        /// </summary>
        protected async Task<ScriptTestResult> ExecuteScriptFromFileAsync(
            string scriptFilePath,
            WorkItem workItem = null,
            string eventType = "workitem.updated",
            string project = null,
            CancellationToken cancellationToken = default)
        {
            return await TestRunner.ExecuteScriptFromFileAsync(scriptFilePath, workItem, eventType, project, cancellationToken);
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
        /// Create a test context for script execution
        /// </summary>
        protected IContext CreateTestContext(WorkItem workItem = null, string eventType = "workitem.updated", string project = null)
        {
            return TestRunner.CreateTestContext(workItem, eventType, project);
        }

        /// <summary>
        /// Create a context that simulates a field change event
        /// </summary>
        protected IContext CreateFieldChangeContext(WorkItem workItem, string fieldName, object oldValue, object newValue)
        {
            return TestRunner.CreateFieldChangeContext(workItem, fieldName, oldValue, newValue);
        }

        /// <summary>
        /// Create a context that simulates a state change event
        /// </summary>
        protected IContext CreateStateChangeContext(WorkItem workItem, string oldState, string newState)
        {
            return TestRunner.CreateStateChangeContext(workItem, oldState, newState);
        }

        /// <summary>
        /// Create a context that simulates a work item creation event
        /// </summary>
        protected IContext CreateWorkItemCreatedContext(WorkItem workItem)
        {
            return TestRunner.CreateWorkItemCreatedContext(workItem);
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
                        [""System.TeamProject""] = Project ?? Client.Project
                    }}
                }};
                
                await Client.SaveWorkItem(newWorkItem);
                Logger.Information($""Created work item {{newWorkItem.Id}}: {title}"");
            ";
        }

        /// <summary>
        /// Helper to create a script that queries work items by type using WIQL
        /// </summary>
        protected string CreateWorkItemQueryScript(string workItemType)
        {
            return $@"
                var queryParams = new QueryLinksByWiqlPrms
                {{
                    Wiql = ""SELECT [System.Id] FROM WorkItems WHERE [System.WorkItemType] = '{workItemType}'""
                }};
                
                var workItems = await Client.QueryLinksByWiql(queryParams);
                Logger.Information($""Found {{workItems.Count}} {workItemType} items"");
                
                foreach (var item in workItems)
                {{
                    Logger.Information($""{workItemType}: {{item.Id}} - {{item.GetField<string>(""""System.Title"""")}}"");
                }}
            ";
        }

        /// <summary>
        /// Helper to create a script that updates the current work item (Self)
        /// </summary>
        protected string CreateSelfUpdateScript(string newTitle, string newState = null)
        {
            var stateUpdate = newState != null ? $@"Self.SetField(""System.State"", ""{newState}"");" : "";
            
            return $@"
                Logger.Information($""Updating work item {{Self.Id}}"");
                Self.SetField(""System.Title"", ""{newTitle}"");
                {stateUpdate}
                Logger.Information($""Updated work item {{Self.Id}} title to '{newTitle}'"");
            ";
        }

        /// <summary>
        /// Helper to create a script that responds to specific events
        /// </summary>
        protected string CreateEventResponseScript(string eventType, string response)
        {
            return $@"
                Logger.Information($""Received event: {{EventType}}"");
                if (EventType == ""{eventType}"")
                {{
                    Logger.Information(""{response}"");
                }}
                else
                {{
                    Logger.Information($""Ignoring event type: {{EventType}}"");
                }}
            ";
        }

        /// <summary>
        /// Helper to create a script that responds to field changes
        /// </summary>
        protected string CreateFieldChangeResponseScript(string fieldName, string response)
        {
            return $@"
                Logger.Information($""Checking for changes to field: {fieldName}"");
                if (SelfChanges.ContainsKey(""{fieldName}""))
                {{
                    var change = SelfChanges[""{fieldName}""];
                    Logger.Information($""Field {fieldName} changed"");
                    Logger.Information(""{response}"");
                }}
                else
                {{
                    Logger.Information($""No changes detected for field: {fieldName}"");
                }}
            ";
        }

        public virtual void Dispose()
        {
            TestRunner?.Dispose();
        }
    }
}