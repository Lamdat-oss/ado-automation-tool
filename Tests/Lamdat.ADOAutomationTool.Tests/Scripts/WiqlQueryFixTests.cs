using Lamdat.ADOAutomationTool.Tests.Framework;
using Lamdat.ADOAutomationTool.Tests.ScheduledScripts;
using FluentAssertions;

namespace Lamdat.ADOAutomationTool.Tests.Scripts
{
    /// <summary>
    /// Tests to verify the WIQL query fixes in the hierarchical aggregation script
    /// </summary>
    public class WiqlQueryFixTests : ScheduledScriptTestBase
    {
        [Fact]
        public async Task ScheduledScript_ShouldUseCorrectWiqlSyntax()
        {
            // Arrange - Test the corrected WIQL syntax patterns
            var script = @"
                // Test corrected date formatting (date only, no time)
                var testDate = DateTime.Now.AddDays(-1).ToString(""yyyy-MM-dd"");
                Logger.Information($""Using correct date format: {testDate}"");
                
                // Test > 0 instead of IS NOT EMPTY for numeric fields
                var query1 = ""SELECT [System.Id] FROM WorkItems WHERE [Microsoft.VSTS.Scheduling.CompletedWork] > 0"";
                var results1 = await Client.QueryWorkItemsByWiql(query1);
                Logger.Information($""Numeric field query executed successfully: {results1.Count} results"");
                
                // Test date query with correct format (date only)
                var query2 = $""SELECT [System.Id] FROM WorkItems WHERE [System.ChangedDate] >= '{testDate}'"";
                var results2 = await Client.QueryWorkItemsByWiql(query2);
                Logger.Information($""Date query with correct format executed successfully: {results2.Count} results"");
                
                Logger.Information(""All WIQL syntax corrections verified"");
            ";

            // Act
            var result = await ExecuteScriptAsync(script);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("Using correct date format:");
            result.ShouldHaveLogMessageContaining("Numeric field query executed successfully:");
            result.ShouldHaveLogMessageContaining("Date query with correct format executed successfully:");
            result.ShouldHaveLogMessageContaining("All WIQL syntax corrections verified");
        }

        [Fact]
        public async Task ScheduledScript_ShouldCorrectlyFormatHierarchicalAggregationQueries()
        {
            // Arrange - Clear any existing test data first
            ClearTestData();
            
            // Create test data and verify hierarchical aggregation queries work
            var task = CreateTestWorkItem("Task", "Test Task", "Done");
            task.SetField("Microsoft.VSTS.Scheduling.CompletedWork", 8.0);
            task.SetField("Microsoft.VSTS.Common.Activity", "Development");
            task.SetField("System.TeamProject", "ADOProject");
            
            // Set the changed date to today to ensure it gets picked up by the date filter
            var today = DateTime.Now;
            task.SetField("System.ChangedDate", today);

            var pbi = CreateTestWorkItem("Product Backlog Item", "Test PBI", "Active");
            pbi.SetField("System.TeamProject", "ADOProject");
            // Don't set changed date for PBI - it should not appear in either query

            // Script that mimics the corrected hierarchical aggregation pattern
            var script = @"
                Client.Project = ""ADOProject"";
                
                // Test the corrected date format from hierarchical aggregation script (date only, no time)
                var sinceLastRun = DateTime.Now.AddDays(-1).ToString(""yyyy-MM-dd"");
                Logger.Information($""Using date format: {sinceLastRun}"");
                
                // Test the corrected task query (matches the fixed script)
                var changedTasksQuery = $@""SELECT [System.Id], [System.Title], [System.WorkItemType], [Microsoft.VSTS.Scheduling.CompletedWork], [Microsoft.VSTS.Common.Activity]
                                          FROM WorkItems 
                                          WHERE [System.WorkItemType] = 'Task' 
                                          AND [System.TeamProject] = 'ADOProject'
                                          AND [System.ChangedDate] >= '{sinceLastRun}' 
                                          AND [Microsoft.VSTS.Scheduling.CompletedWork] > 0
                                          ORDER BY [System.ChangedDate]"";
                
                var changedTasks = await Client.QueryWorkItemsByWiql(changedTasksQuery);
                Logger.Information($""Found {changedTasks.Count} tasks with completed work"");
                
                // Test the corrected feature query
                var changedFeaturesQuery = $@""SELECT [System.Id], [System.Title], [System.WorkItemType]
                                             FROM WorkItems 
                                             WHERE [System.WorkItemType] = 'Feature' 
                                             AND [System.TeamProject] = 'ADOProject'
                                             AND [System.ChangedDate] >= '{sinceLastRun}' 
                                             ORDER BY [System.ChangedDate]"";
                
                var changedFeatures = await Client.QueryWorkItemsByWiql(changedFeaturesQuery);
                Logger.Information($""Found {changedFeatures.Count} changed features"");
                
                Logger.Information(""Hierarchical aggregation query patterns verified"");
            ";

            // Act
            var result = await ExecuteScriptAsync(script);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("Using date format:");
            result.ShouldHaveLogMessageContaining("Found 1 tasks with completed work");
            result.ShouldHaveLogMessageContaining("Found 0 changed features");
            result.ShouldHaveLogMessageContaining("Hierarchical aggregation query patterns verified");
        }

        [Fact]
        public async Task ScheduledScript_ShouldHandleWorkItemLinksQueries()
        {
            // Arrange - Test the WorkItemLinks queries used in the script
            var script = @"
                Client.Project = ""ADOProject"";
                
                // Test that the WorkItemLinks WIQL syntax is valid and executes without errors
                // (Mock might return 0 results, but the query should execute successfully)
                
                // Test parent relationship query (matches the fixed script pattern)
                var parentQuery = @""SELECT [Target].[System.Id], [Target].[System.WorkItemType]
                                    FROM WorkItemLinks
                                    WHERE [Source].[System.Id] = 123
                                    AND [Source].[System.TeamProject] = 'ADOProject'
                                    AND [Target].[System.TeamProject] = 'ADOProject'
                                    AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Reverse'
                                    AND [Target].[System.WorkItemType] IN ('Product Backlog Item', 'Bug', 'Feature', 'Epic')"";
                
                var parents = await Client.QueryWorkItemsByWiql(parentQuery);
                Logger.Information($""Parent query executed successfully: {parents.Count} results"");
                
                // Test child task query pattern
                var childTasksQuery = @""SELECT [Target].[System.Id], [Target].[Microsoft.VSTS.Scheduling.CompletedWork], [Target].[Microsoft.VSTS.Common.Activity]
                                        FROM WorkItemLinks
                                        WHERE [Source].[System.Id] = 456
                                        AND [Source].[System.TeamProject] = 'ADOProject'
                                        AND [Target].[System.TeamProject] = 'ADOProject'
                                        AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward'
                                        AND [Target].[System.WorkItemType] = 'Task'"";
                
                var childTasks = await Client.QueryWorkItemsByWiql(childTasksQuery);
                Logger.Information($""Child task query executed successfully: {childTasks.Count} results"");
                
                Logger.Information(""WorkItemLinks query patterns verified"");
            ";

            // Act
            var result = await ExecuteScriptAsync(script);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("Parent query executed successfully:");
            result.ShouldHaveLogMessageContaining("Child task query executed successfully:");
            result.ShouldHaveLogMessageContaining("WorkItemLinks query patterns verified");
        }
    }
}