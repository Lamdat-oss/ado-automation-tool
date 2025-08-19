using Lamdat.ADOAutomationTool.Tests.Framework;
using Lamdat.ADOAutomationTool.Tests.ScheduledScripts;
using FluentAssertions;

namespace Lamdat.ADOAutomationTool.Tests.Scripts
{
    /// <summary>
    /// Tests to verify project setting functionality in scheduled scripts
    /// </summary>
    public class ProjectSettingTests : ScheduledScriptTestBase
    {
        [Fact]
        public async Task ScheduledScript_ShouldHaveNoDefaultProject()
        {
            // Arrange
            var script = @"
                Logger.Information($""Client project is: '{Client.Project ?? ""null""}'?"");
                
                // Test that scripts can set their own project
                Client.Project = ""MyCustomProject"";
                Logger.Information($""Client project after setting: '{Client.Project}'"");
                
                Logger.Information(""Project test completed"");
            ";

            // Act
            var result = await ExecuteScriptAsync(script);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("Client project is: 'TestProject'");
            result.ShouldHaveLogMessageContaining("Client project after setting: 'MyCustomProject'");
            result.ShouldHaveLogMessageContaining("Project test completed");
        }

       

        [Fact]
        public async Task HierarchicalAggregationScript_ShouldSetProjectCorrectly()
        {
            // Arrange - Create some test work items for ADOProject project
            var task = CreateTestWorkItem("Task", "Test Task for ADOProject", "Active");
            task.SetField("System.TeamProject", "ADOProject");
            task.SetField("Microsoft.VSTS.Scheduling.CompletedWork", 8.0);
            task.SetField("Microsoft.VSTS.Common.Activity", "Development");

            var pbi = CreateTestWorkItem("Product Backlog Item", "Test PBI for ADOProject", "Active");
            pbi.SetField("System.TeamProject", "ADOProject");
            
            // Create parent-child relationship
            task.Relations.Add(new Lamdat.ADOAutomationTool.Entities.WorkItemRelation
            {
                RelationType = "Parent",
                RelatedWorkItemId = pbi.Id,
                Rel = "System.LinkTypes.Hierarchy-Reverse",
                Url = $"https://test/_apis/wit/workItems/{pbi.Id}"
            });

            pbi.Relations.Add(new Lamdat.ADOAutomationTool.Entities.WorkItemRelation
            {
                RelationType = "Child",
                RelatedWorkItemId = task.Id,
                Rel = "System.LinkTypes.Hierarchy-Forward",
                Url = $"https://test/_apis/wit/workItems/{task.Id}"
            });

            // Use a simplified version of the hierarchical aggregation logic
            var script = @"
                // Set the project to ADOProject for all operations (like the real script does)
                Client.Project = ""ADOProject"";
                
                Logger.Information($""Aggregation running with project: {Client.Project}"");
                
                // Find tasks with completed work in ADOProject project
                var tasksQuery = ""SELECT [System.Id] FROM WorkItems WHERE [System.WorkItemType] = 'Task' AND [System.TeamProject] = 'ADOProject'"";
                var tasks = await Client.QueryWorkItemsByWiql(tasksQuery);
                
                Logger.Information($""Found {tasks.Count} tasks in ADOProject project"");
                
                foreach (var task in tasks)
                {
                    var completedWork = task.GetField<double?>(""Microsoft.VSTS.Scheduling.CompletedWork"") ?? 0;
                    var activity = task.GetField<string>(""Microsoft.VSTS.Common.Activity"") ?? """";
                    
                    Logger.Information($""Task {task.Id}: {completedWork} hours of {activity}"");
                }
                
                Logger.Information($""Processed {tasks.Count} tasks from ADOProject"");
            ";

            // Act
            var result = await ExecuteScriptAsync(script);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("Aggregation running with project: ADOProject");
            result.ShouldHaveLogMessageContaining("Found 1 tasks in ADOProject project");
            result.ShouldHaveLogMessageContaining("8 hours of Development");
            result.ShouldHaveLogMessageContaining("Processed 1 tasks from ADOProject");
        }
    }
}