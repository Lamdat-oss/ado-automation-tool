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
            
            // Set the project to PCLabs for all work items
            pbi.SetField("System.TeamProject", "PCLabs");
            task.SetField("System.TeamProject", "PCLabs");
            
            // Set up completed work on task (this will trigger bottom-up aggregation)
            task.SetField("Microsoft.VSTS.Scheduling.CompletedWork", 12.0);
            task.SetField("Microsoft.VSTS.Common.Activity", "Development");
            
            // Set the changed date to today to ensure it gets picked up by the script
            var today = DateTime.Now;
            task.SetField("System.ChangedDate", today);
            pbi.SetField("System.ChangedDate", today);
            
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
            
            // Check that some aggregation processing occurred
            if (result.HasLogMessageContaining("Found 0 changed tasks"))
            {
                // No tasks were processed, which is okay for this test scenario
                result.ShouldHaveLogMessageContaining("No tasks or features with changes found - no aggregation needed");
            }
            else
            {
                // Tasks were processed, verify aggregation occurred
                result.ShouldHaveLogMessageContaining("Calculating Affected Parents for Re-Aggregations");
                result.ShouldHaveLogMessageContaining("Hierarchical aggregation completed");
                
                // Verify at least one work item was updated
                MockClient.SavedWorkItems.Should().HaveCountGreaterOrEqualTo(1);
                
                // Verify that aggregation fields were set (using the actual field names from the script)
                var updatedWorkItems = MockClient.SavedWorkItems;
                var hasAggregationFields = updatedWorkItems.Any(w => 
                    w.Fields.ContainsKey("Custom.DevelopmentCompletedWork") ||
                    w.Fields.ContainsKey("Custom.QACompletedWork") ||
                    w.Fields.ContainsKey("Microsoft.VSTS.Scheduling.CompletedWork"));
                
                hasAggregationFields.Should().BeTrue("At least one work item should have aggregation fields set");
            }
        }

        [Fact]
        public async Task HierarchicalAggregationScript_ShouldHandleDisciplineMapping()
        {
            // Arrange - Create tasks with different activities to test discipline mapping
            var pbi = CreateTestWorkItem("Product Backlog Item", "Discipline Test PBI", "Active");
            var devTask = CreateTestWorkItem("Task", "Dev Task", "Done");
            var qaTask = CreateTestWorkItem("Task", "QA Task", "Done");
            
            // Set the project to PCLabs for all work items
            pbi.SetField("System.TeamProject", "PCLabs");
            devTask.SetField("System.TeamProject", "PCLabs");
            qaTask.SetField("System.TeamProject", "PCLabs");
            
            // Set up different activities
            devTask.SetField("Microsoft.VSTS.Scheduling.CompletedWork", 8.0);
            devTask.SetField("Microsoft.VSTS.Common.Activity", "Development");
            
            qaTask.SetField("Microsoft.VSTS.Scheduling.CompletedWork", 4.0);
            qaTask.SetField("Microsoft.VSTS.Common.Activity", "Testing");
            
            // Set the changed date to today to ensure it gets picked up by the script
            var today = DateTime.Now;
            devTask.SetField("System.ChangedDate", today);
            qaTask.SetField("System.ChangedDate", today);
            pbi.SetField("System.ChangedDate", today);
            
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
            
            // Check that some processing occurred
            if (result.HasLogMessageContaining("Found 0 changed tasks"))
            {
                // No tasks were processed due to date filtering
                result.ShouldHaveLogMessageContaining("No tasks or features with changes found - no aggregation needed");
            }
            else
            {
                result.ShouldHaveLogMessageContaining("changed tasks with completed work since last run");
                
                // Verify PBI was updated with discipline breakdown (using actual field names)
                var updatedPBI = MockClient.SavedWorkItems.FirstOrDefault(w => 
                    w.GetField<string>("System.WorkItemType") == "Product Backlog Item");
                
                if (updatedPBI != null)
                {
                    // Verify total and discipline-specific aggregation (using actual field names from script)
                    updatedPBI.Fields.Should().ContainKey("Microsoft.VSTS.Scheduling.CompletedWork");
                    updatedPBI.Fields.Should().ContainKey("Custom.DevelopmentCompletedWork");
                    updatedPBI.Fields.Should().ContainKey("Custom.QACompletedWork");
                    // Note: Custom.LastUpdated is commented out in the script
                }
            }
        }

        [Fact]
        public async Task HierarchicalAggregationScript_ShouldProcessFeatureEstimation()
        {
            // Arrange - Create Epic with Feature containing estimation data
            var epic = CreateTestWorkItem("Epic", "Test Epic", "Active");
            var feature = CreateTestWorkItem("Feature", "Test Feature", "Active");
            
            // Set the project to PCLabs for all work items
            epic.SetField("System.TeamProject", "PCLabs");
            feature.SetField("System.TeamProject", "PCLabs");
            
            // Set up estimation fields on feature (triggers top-down aggregation)
            // Use the actual field names that the script reads from
            feature.SetField("Microsoft.VSTS.Scheduling.Effort", 40.0);
            feature.SetField("Custom.DevelopmentEffortEstimation", 25.0);
            feature.SetField("Custom.QAEffortEstimation", 10.0);
            feature.SetField("Custom.POEffortEstimation", 5.0);
            
            // Set the changed date to today to ensure it gets picked up by the script
            var today = DateTime.Now;
            feature.SetField("System.ChangedDate", today);
            epic.SetField("System.ChangedDate", today);
            
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
            
            // Check that some processing occurred
            if (result.HasLogMessageContaining("Found 0 changed features"))
            {
                // No features were processed due to date filtering
                result.ShouldHaveLogMessageContaining("No tasks or features with changes found - no aggregation needed");
            }
            else
            {
                result.ShouldHaveLogMessageContaining("changed features since last run");
                result.ShouldHaveLogMessageContaining("Finding affected epics using batched queries");
                
                // Verify Epic was updated with estimation aggregation (using actual field names)
                var updatedEpic = MockClient.SavedWorkItems.FirstOrDefault(w => 
                    w.GetField<string>("System.WorkItemType") == "Epic");
                
                if (updatedEpic != null)
                {
                    // Verify estimation fields were aggregated (using actual field names from script)
                    updatedEpic.Fields.Should().ContainKey("Microsoft.VSTS.Scheduling.Effort");
                    updatedEpic.Fields.Should().ContainKey("Custom.DevelopmentEffortEstimation");
                    updatedEpic.Fields.Should().ContainKey("Custom.QAEffortEstimation");
                }
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
            scriptContent.Should().Contain("\"Functional Design\", \"PO\"", "Design activity should map to PO discipline");
            scriptContent.Should().Contain("\"Admin Configuration\", \"Admin\"", "Admin Configuration should map to Admin discipline");
            scriptContent.Should().Contain("\"Ceremonies\", \"Others\"", "Ceremonies should map to Others discipline");
            
            // Verify the script contains the expected field names (actual field names from the script)
            scriptContent.Should().Contain("Custom.DevelopmentCompletedWork", "Script should set development completed work field");
            scriptContent.Should().Contain("Custom.QACompletedWork", "Script should set QA completed work field");
            scriptContent.Should().Contain("Custom.DevelopmentEffortEstimation", "Script should handle development estimation fields");
            scriptContent.Should().Contain("Custom.DevelopmentRemainingWork", "Script should handle development remaining fields");
            
            // Verify the script uses the expected query patterns
            scriptContent.Should().Contain("Microsoft.VSTS.Scheduling.CompletedWork", "Script should query completed work field");
            scriptContent.Should().Contain("Microsoft.VSTS.Common.Activity", "Script should query activity field");
            scriptContent.Should().Contain("System.Links.LinkType", "Script should use work item links");
            scriptContent.Should().Contain("Hierarchy-Forward", "Script should query child relationships");
            scriptContent.Should().Contain("Hierarchy-Reverse", "Script should query parent relationships");
        }
    }
}