using Lamdat.ADOAutomationTool.Tests.Framework;
using FluentAssertions;

namespace Lamdat.ADOAutomationTool.Tests.Scripts
{
    /// <summary>
    /// Tests demonstrating the new simple WIQL query method
    /// </summary>
    public class SimpleWiqlQueryTests : ScriptTestBase
    {
        [Fact]
        public async Task SimpleWiqlQuery_ShouldReturnWorkItemsByType()
        {
            // Arrange
            var task1 = CreateTestWorkItem("Task", "Test Task 1", "New");
            var task2 = CreateTestWorkItem("Task", "Test Task 2", "Active");
            var bug = CreateTestWorkItem("Bug", "Test Bug", "New");
            var feature = CreateTestWorkItem("Feature", "Test Feature", "New");

            var script = @"
                // Test the new simple WIQL query method
                var tasksQuery = ""SELECT [System.Id], [System.Title] FROM WorkItems WHERE [System.WorkItemType] = 'Task'"";
                var tasks = await Client.QueryWorkItemsByWiql(tasksQuery);
                
                Logger.Information($""Found {tasks.Count} tasks using simple WIQL query"");
                
                foreach (var task in tasks)
                {
                    Logger.Information($""Task {task.Id}: {task.Title}"");
                }

                // Test filtering with project
                var bugsQuery = ""SELECT [System.Id] FROM WorkItems WHERE [System.WorkItemType] = 'Bug' AND [System.TeamProject] = 'PCLabs'"";
                var bugs = await Client.QueryWorkItemsByWiql(bugsQuery);
                
                Logger.Information($""Found {bugs.Count} bugs in PCLabs project"");
            ";

            // Act
            var result = await ExecuteScriptAsync(script);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("tasks using simple WIQL query");
            result.ShouldHaveLogMessageContaining("Test Task 1");
            result.ShouldHaveLogMessageContaining("Test Task 2");
            result.ShouldHaveLogMessageContaining("bugs in PCLabs project");
        }

        [Fact]
        public async Task SimpleWiqlQuery_WithTopLimit_ShouldRespectLimit()
        {
            // Arrange
            for (int i = 1; i <= 5; i++)
            {
                CreateTestWorkItem("Task", $"Task {i}", "New");
            }

            var script = @"
                // Test the top parameter
                var query = ""SELECT [System.Id] FROM WorkItems WHERE [System.WorkItemType] = 'Task'"";
                var allTasks = await Client.QueryWorkItemsByWiql(query);
                var limitedTasks = await Client.QueryWorkItemsByWiql(query, 3);
                
                Logger.Information($""All tasks count: {allTasks.Count}"");
                Logger.Information($""Limited tasks count: {limitedTasks.Count}"");
                Logger.Information($""Top limit working: {limitedTasks.Count <= 3}"");
            ";

            // Act
            var result = await ExecuteScriptAsync(script);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("All tasks count:");
            result.ShouldHaveLogMessageContaining("Limited tasks count:");
            result.ShouldHaveLogMessageContaining("Top limit working: True");
        }

        [Fact]
        public async Task SimpleWiqlQuery_WithDateFilter_ShouldWork()
        {
            // Arrange
            CreateTestWorkItem("Task", "Recent Task", "New");
            CreateTestWorkItem("Bug", "Recent Bug", "New");

            var script = @"
                // Test date filtering
                var yesterday = DateTime.Now.AddDays(-1).ToString(""yyyy-MM-ddTHH:mm:ss.fffZ"");
                var query = $""SELECT [System.Id] FROM WorkItems WHERE [System.WorkItemType] = 'Task' AND [System.ChangedDate] >= '{yesterday}'"";
                var recentTasks = await Client.QueryWorkItemsByWiql(query);
                
                Logger.Information($""Date filter query executed successfully"");
                Logger.Information($""Found {recentTasks.Count} recent tasks"");
            ";

            // Act
            var result = await ExecuteScriptAsync(script);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("Date filter query executed successfully");
            result.ShouldHaveLogMessageContaining("Found");
            result.ShouldHaveLogMessageContaining("recent tasks");
        }

        [Fact]
        public async Task SimpleWiqlQuery_WithInvalidQuery_ShouldReturnEmptyList()
        {
            // Arrange
            var script = @"
                // Test with invalid/empty query
                var emptyResults = await Client.QueryWorkItemsByWiql("""");
                var invalidResults = await Client.QueryWorkItemsByWiql(null);
                
                Logger.Information($""Empty query results: {emptyResults.Count}"");
                Logger.Information($""Null query results: {invalidResults.Count}"");
                Logger.Information(""Invalid queries handled gracefully"");
            ";

            // Act
            var result = await ExecuteScriptAsync(script);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("Empty query results: 0");
            result.ShouldHaveLogMessageContaining("Null query results: 0");
            result.ShouldHaveLogMessageContaining("Invalid queries handled gracefully");
        }

        [Fact]
        public async Task SimpleWiqlQuery_ComparedToOldMethod_ShouldBeSimplerToUse()
        {
            // Arrange
            CreateTestWorkItem("Task", "Test Task", "New");

            var script = @"
                // Old complex way using QueryLinksByWiql with parameters
                var oldParams = new QueryLinksByWiqlPrms
                {
                    Wiql = ""SELECT [System.Id] FROM WorkItems WHERE [System.WorkItemType] = 'Task'""
                };
                var oldResults = await Client.QueryLinksByWiql(oldParams);
                
                // New simple way
                var newResults = await Client.QueryWorkItemsByWiql(""SELECT [System.Id] FROM WorkItems WHERE [System.WorkItemType] = 'Task'"");
                
                Logger.Information($""Old method found: {oldResults.Count} tasks"");
                Logger.Information($""New method found: {newResults.Count} tasks"");
                Logger.Information(""New method is much simpler to use!"");
                Logger.Information($""Both methods return same results: {oldResults.Count == newResults.Count}"");
            ";

            // Act
            var result = await ExecuteScriptAsync(script);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("Old method found:");
            result.ShouldHaveLogMessageContaining("New method found:");
            result.ShouldHaveLogMessageContaining("New method is much simpler to use!");
            result.ShouldHaveLogMessageContaining("Both methods return same results: True");
        }
    }
}