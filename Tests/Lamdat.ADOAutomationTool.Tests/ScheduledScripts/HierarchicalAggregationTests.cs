using Lamdat.ADOAutomationTool.Tests.Framework;
using Lamdat.ADOAutomationTool.Entities;
using FluentAssertions;

namespace Lamdat.ADOAutomationTool.Tests.ScheduledScripts
{
    /// <summary>
    /// Tests for the actual hierarchical work item aggregation script (08-hierarchical-aggregation.rule)
    /// These tests execute the real script file to ensure it works correctly.
    /// </summary>
    public class HierarchicalAggregationScriptTests : ScheduledScriptTestBase
    {
        private static string AGGREGATION_SCRIPT_PATH
        {
            get
            {
                // Get the solution directory and build the path to the script
                var currentDirectory = Directory.GetCurrentDirectory();
                var solutionDirectory = currentDirectory;
                
                // Navigate up to find the solution root (where .git or .sln might be)
                while (solutionDirectory != null && !Directory.Exists(Path.Combine(solutionDirectory, "Src")))
                {
                    solutionDirectory = Directory.GetParent(solutionDirectory)?.FullName;
                }
                
                if (solutionDirectory == null)
                {
                    throw new DirectoryNotFoundException("Could not find solution directory containing 'Src' folder");
                }
                
                return Path.Combine(solutionDirectory, "Src", "Lamdat.ADOAutomationTool", "scheduled-scripts", "08-hierarchical-aggregation.rule");
            }
        }

        [Fact]
        public async Task HierarchicalAggregationScript_ShouldLoadAndExecuteSuccessfully()
        {
            // Arrange - Verify the script file exists and can be loaded
            AGGREGATION_SCRIPT_PATH.Should().NotBeNull();
            File.Exists(AGGREGATION_SCRIPT_PATH).Should().BeTrue($"Script file should exist at {AGGREGATION_SCRIPT_PATH}");
            
            // Act - Execute the actual hierarchical aggregation script (with no test data)
            var result = await ExecuteScriptFromFileAsync(AGGREGATION_SCRIPT_PATH);
            
            // Assert - Verify the script loads, compiles and executes without errors
            result.ShouldBeSuccessful();
            result.ShouldBeIntervalAware();
            result.ShouldHaveNextInterval(10);
            result.ShouldReturnSuccessfulResult();
            
            // Verify the script uses the expected structure and messages
            result.ShouldHaveLogMessageContaining("Starting hierarchical work item aggregation");
            result.ShouldHaveLogMessageContaining("Processing changes since:");
            result.ShouldHaveLogMessageContaining("Aggregation running as:");
            result.ShouldHaveLogMessageContaining("No tasks or features with changes found - no aggregation needed");
            
            // Verify it returns the expected success message when no work to do
            result.ScheduledScriptResult.Should().NotBeNull();
            result.ScheduledScriptResult.Message.Should().Contain("No aggregation needed");
        }

        [Fact]
        public async Task HierarchicalAggregationScript_ShouldProcessTasksAndCreateAggregation()
        {
            // Arrange - Create a simple hierarchy to test the core aggregation logic
            var pbi = CreateTestWorkItem("Product Backlog Item", "Test PBI", "Active");
            var task = CreateTestWorkItem("Task", "Development Task", "Done");
            
            // Set up completed work on task (this will trigger bottom-up aggregation)
            task.SetField("Microsoft.VSTS.Scheduling.CompletedWork", 12.0);
            task.SetField("Microsoft.VSTS.Common.Activity", "Development");
            
            // Set up parent-child relationship
            pbi.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = task.Id });
            
            // Save work items
            await MockClient.SaveWorkItem(pbi);
            await MockClient.SaveWorkItem(task);
            
            // Clear to track script saves only
            MockClient.SavedWorkItems.Clear();
            
            // Act - Execute the script
            var result = await ExecuteScriptFromFileAsync(AGGREGATION_SCRIPT_PATH);
            
            // Assert - Verify the script processed the work items
            result.ShouldBeSuccessful();
            result.ShouldBeIntervalAware();
            result.ShouldReturnSuccessfulResult();
            
            // Verify the script found and processed the task
            result.ShouldHaveLogMessageContaining("Starting hierarchical work item aggregation");
            result.ShouldHaveLogMessageContaining("changed tasks with completed work since last run");
            result.ShouldHaveLogMessageContaining("parent work items affected by task changes");
            result.ShouldHaveLogMessageContaining("Hierarchical aggregation completed");
            result.ShouldHaveLogMessageContaining("Tasks processed:");
            
            // Verify at least one work item was updated
            MockClient.SavedWorkItems.Should().HaveCountGreaterOrEqualTo(1);
            
            // Verify that aggregation fields were set
            var updatedWorkItems = MockClient.SavedWorkItems;
            var hasAggregationFields = updatedWorkItems.Any(w => 
                w.Fields.ContainsKey("Custom.Aggregation.TotalCompletedWork") ||
                w.Fields.ContainsKey("Custom.Aggregation.LastUpdated"));
            
            hasAggregationFields.Should().BeTrue("At least one work item should have aggregation fields set");
        }

        [Fact]
        public async Task HierarchicalAggregationScript_ShouldHandleDisciplineMapping()
        {
            // Arrange - Create tasks with different activities to test discipline mapping
            var pbi = CreateTestWorkItem("Product Backlog Item", "Discipline Test PBI", "Active");
            var devTask = CreateTestWorkItem("Task", "Dev Task", "Done");
            var qaTask = CreateTestWorkItem("Task", "QA Task", "Done");
            
            // Set up different activities
            devTask.SetField("Microsoft.VSTS.Scheduling.CompletedWork", 8.0);
            devTask.SetField("Microsoft.VSTS.Common.Activity", "Development");
            
            qaTask.SetField("Microsoft.VSTS.Scheduling.CompletedWork", 4.0);
            qaTask.SetField("Microsoft.VSTS.Common.Activity", "Testing");
            
            // Set up relationships
            pbi.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = devTask.Id });
            pbi.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = qaTask.Id });
            
            // Save work items
            await MockClient.SaveWorkItem(pbi);
            await MockClient.SaveWorkItem(devTask);
            await MockClient.SaveWorkItem(qaTask);
            
            MockClient.SavedWorkItems.Clear();
            
            // Act
            var result = await ExecuteScriptFromFileAsync(AGGREGATION_SCRIPT_PATH);
            
            // Assert - Verify discipline-specific aggregation occurred
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("changed tasks with completed work since last run");
            
            // Verify PBI was updated with discipline breakdown
            var updatedPBI = MockClient.SavedWorkItems.FirstOrDefault(w => 
                w.GetField<string>("System.WorkItemType") == "Product Backlog Item");
            
            if (updatedPBI != null)
            {
                // Verify total and discipline-specific aggregation
                updatedPBI.Fields.Should().ContainKey("Custom.Aggregation.TotalCompletedWork");
                updatedPBI.Fields.Should().ContainKey("Custom.Aggregation.DevelopmentCompletedWork");
                updatedPBI.Fields.Should().ContainKey("Custom.Aggregation.QACompletedWork");
                updatedPBI.Fields.Should().ContainKey("Custom.Aggregation.LastUpdated");
            }
        }

        [Fact]
        public async Task HierarchicalAggregationScript_ShouldProcessFeatureEstimation()
        {
            // Arrange - Create Epic with Feature containing estimation data
            var epic = CreateTestWorkItem("Epic", "Test Epic", "Active");
            var feature = CreateTestWorkItem("Feature", "Test Feature", "Active");
            
            // Set up estimation fields on feature (triggers top-down aggregation)
            feature.SetField("Custom.Estimation.TotalEffortEstimation", 40.0);
            feature.SetField("Custom.Estimation.DevelopmentEffortEstimation", 25.0);
            feature.SetField("Custom.Estimation.QAEffortEstimation", 10.0);
            feature.SetField("Custom.Estimation.POEffortEstimation", 5.0);
            
            // Set up hierarchy
            epic.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = feature.Id });
            
            // Save work items
            await MockClient.SaveWorkItem(epic);
            await MockClient.SaveWorkItem(feature);
            
            MockClient.SavedWorkItems.Clear();
            
            // Act
            var result = await ExecuteScriptFromFileAsync(AGGREGATION_SCRIPT_PATH);
            
            // Assert - Verify top-down aggregation occurred
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("changed features since last run");
            result.ShouldHaveLogMessageContaining("epic work items affected by feature changes");
            
            // Verify Epic was updated with estimation aggregation
            var updatedEpic = MockClient.SavedWorkItems.FirstOrDefault(w => 
                w.GetField<string>("System.WorkItemType") == "Epic");
            
            if (updatedEpic != null)
            {
                // Verify estimation fields were aggregated
                updatedEpic.Fields.Should().ContainKey("Custom.Estimation.TotalEffortEstimation");
                updatedEpic.Fields.Should().ContainKey("Custom.Estimation.DevelopmentEffortEstimation");
                updatedEpic.Fields.Should().ContainKey("Custom.Aggregation.LastUpdated");
            }
        }

        [Fact]
        public async Task HierarchicalAggregationScript_ShouldUseCorrectActivityMappings()
        {
            // This test verifies that the script contains the expected activity-to-discipline mappings
            // by reading the script content directly
            
            // Arrange & Act - Read the script file content
            var scriptContent = await File.ReadAllTextAsync(AGGREGATION_SCRIPT_PATH);
            
            // Assert - Verify key activity mappings are present in the script
            scriptContent.Should().Contain("\"Development\", \"Development\"", "Development activity should map to Development discipline");
            scriptContent.Should().Contain("\"Testing\", \"QA\"", "Testing activity should map to QA discipline");
            scriptContent.Should().Contain("\"Design\", \"PO\"", "Design activity should map to PO discipline");
            scriptContent.Should().Contain("\"Admin Configuration\", \"Admin\"", "Admin Configuration should map to Admin discipline");
            scriptContent.Should().Contain("\"Ceremonies\", \"Others\"", "Ceremonies should map to Others discipline");
            
            // Verify the script contains the expected field names
            scriptContent.Should().Contain("Custom.Aggregation.TotalCompletedWork", "Script should set total completed work field");
            scriptContent.Should().Contain("Custom.Aggregation.DevelopmentCompletedWork", "Script should set development completed work field");
            scriptContent.Should().Contain("Custom.Aggregation.QACompletedWork", "Script should set QA completed work field");
            scriptContent.Should().Contain("Custom.Estimation.TotalEffortEstimation", "Script should handle estimation fields");
            scriptContent.Should().Contain("Custom.Remaining.TotalRemainingEstimation", "Script should handle remaining fields");
            
            // Verify the script uses the expected query patterns
            scriptContent.Should().Contain("Microsoft.VSTS.Scheduling.CompletedWork", "Script should query completed work field");
            scriptContent.Should().Contain("Microsoft.VSTS.Common.Activity", "Script should query activity field");
            scriptContent.Should().Contain("System.Links.LinkType", "Script should use work item links");
            scriptContent.Should().Contain("Hierarchy-Forward", "Script should query child relationships");
            scriptContent.Should().Contain("Hierarchy-Reverse", "Script should query parent relationships");
        }
    }
}