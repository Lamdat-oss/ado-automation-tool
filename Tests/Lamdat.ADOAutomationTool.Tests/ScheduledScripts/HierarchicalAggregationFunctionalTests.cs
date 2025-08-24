using Lamdat.ADOAutomationTool.Tests.Framework;
using Lamdat.ADOAutomationTool.Entities;
using FluentAssertions;

namespace Lamdat.ADOAutomationTool.Tests.ScheduledScripts
{
    /// <summary>
    /// Comprehensive functional tests for the hierarchical aggregation script (08-hierarchical-aggregation.rule)
    /// Tests both bottom-up (Task → PBI/Feature/Epic) and top-down (Feature → Epic) aggregation
    /// </summary>
    public class HierarchicalAggregationFunctionalTests : ScheduledScriptTestBase
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
        public async Task HierarchicalAggregation_BottomUp_TaskHours_ShouldAggregateToParents()
        {
            // Arrange - Create Epic > Feature > PBI > Tasks hierarchy with completed work
            ClearTestData();
            
            // Create hierarchy: Epic -> Feature -> PBI -> Tasks
            var epic = CreateTestWorkItem("Epic", "Test Epic", "Active");
            var feature = CreateTestWorkItem("Feature", "Test Feature", "Active");
            var pbi = CreateTestWorkItem("Product Backlog Item", "Test PBI", "Active");
            var task1 = CreateTestWorkItem("Task", "Development Task", "Done");
            var task2 = CreateTestWorkItem("Task", "QA Task", "Done");
            
            // Set all work items to PCLabs project
            epic.SetField("System.TeamProject", "PCLabs");
            feature.SetField("System.TeamProject", "PCLabs");
            pbi.SetField("System.TeamProject", "PCLabs");
            task1.SetField("System.TeamProject", "PCLabs");
            task2.SetField("System.TeamProject", "PCLabs");
            
            // Set up completed work and activities on tasks
            task1.SetField("Microsoft.VSTS.Scheduling.CompletedWork", 8.0);
            task1.SetField("Microsoft.VSTS.Common.Activity", "Development");
            
            task2.SetField("Microsoft.VSTS.Scheduling.CompletedWork", 4.0);
            task2.SetField("Microsoft.VSTS.Common.Activity", "Testing");
            
            // Set changed dates to today to ensure tasks are picked up
            var today = DateTime.Now;
            task1.SetField("System.ChangedDate", today);
            task2.SetField("System.ChangedDate", today);
            
            // Set up parent-child relationships
            // Epic -> Feature
            epic.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = feature.Id });
            
            // Feature -> PBI
            feature.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = pbi.Id });
            
            // PBI -> Tasks
            pbi.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = task1.Id });
            pbi.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = task2.Id });
            
            // Save all work items
            await MockClient.SaveWorkItem(epic);
            await MockClient.SaveWorkItem(feature);
            await MockClient.SaveWorkItem(pbi);
            await MockClient.SaveWorkItem(task1);
            await MockClient.SaveWorkItem(task2);
            
            // Clear saved items to track script updates only
            MockClient.SavedWorkItems.Clear();
            
            // Act - Execute the hierarchical aggregation script
            var result = await ExecuteScriptFromFileAsync(AGGREGATION_SCRIPT_PATH);
            
            // Assert - Verify script execution
            result.ShouldBeSuccessful();
            result.ShouldBeIntervalAware();
            result.ShouldReturnSuccessfulResult();
            
            // Verify aggregation processing occurred
            result.ShouldHaveLogMessageContaining("Starting hierarchical work item aggregation");
            result.ShouldHaveLogMessageContaining("changed tasks with completed work since last run");
            result.ShouldHaveLogMessageContaining("Hierarchical aggregation completed");
            
            // Verify work items were updated
            MockClient.SavedWorkItems.Should().HaveCountGreaterOrEqualTo(3); // PBI, Feature, Epic should be updated
            
            // Get the updated work items from the saved items
            var updatedPBI = MockClient.SavedWorkItems.FirstOrDefault(w => 
                w.GetField<string>("System.WorkItemType") == "Product Backlog Item");
            var updatedFeature = MockClient.SavedWorkItems.FirstOrDefault(w => 
                w.GetField<string>("System.WorkItemType") == "Feature");
            var updatedEpic = MockClient.SavedWorkItems.FirstOrDefault(w => 
                w.GetField<string>("System.WorkItemType") == "Epic");
            
            // Verify PBI aggregation (direct from tasks)
            if (updatedPBI != null)
            {
                updatedPBI.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork").Should().Be(12.0); // 8 + 4
                updatedPBI.GetField<double?>("Custom.DevelopmentCompletedWork").Should().Be(8.0);
                updatedPBI.GetField<double?>("Custom.QACompletedWork").Should().Be(4.0);
                updatedPBI.GetField<double?>("Custom.POCompletedWork").Should().Be(0.0);
                updatedPBI.GetField<double?>("Custom.AdminCompletedWork").Should().Be(0.0);
                updatedPBI.GetField<double?>("Custom.OthersCompletedWork").Should().Be(0.0);
            }
            
            // Verify Feature aggregation (from PBI)
            if (updatedFeature != null)
            {
                updatedFeature.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork").Should().Be(12.0);
                updatedFeature.GetField<double?>("Custom.DevelopmentCompletedWork").Should().Be(8.0);
                updatedFeature.GetField<double?>("Custom.QACompletedWork").Should().Be(4.0);
            }
            
            // Verify Epic aggregation (from Feature)
            if (updatedEpic != null)
            {
                updatedEpic.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork").Should().Be(12.0);
                updatedEpic.GetField<double?>("Custom.DevelopmentCompletedWork").Should().Be(8.0);
                updatedEpic.GetField<double?>("Custom.QACompletedWork").Should().Be(4.0);
            }
        }
        
        [Fact]
        public async Task HierarchicalAggregation_BottomUp_MultiplePBIs_ShouldAggregateCorrectly()
        {
            // Arrange - Create Epic with Feature containing multiple PBIs with tasks
            ClearTestData();
            
            var epic = CreateTestWorkItem("Epic", "Multi-PBI Epic", "Active");
            var feature = CreateTestWorkItem("Feature", "Multi-PBI Feature", "Active");
            var pbi1 = CreateTestWorkItem("Product Backlog Item", "PBI 1", "Active");
            var pbi2 = CreateTestWorkItem("Product Backlog Item", "PBI 2", "Active");
            var bug1 = CreateTestWorkItem("Bug", "Bug 1", "Active");
            
            // Tasks for PBI 1
            var pbi1Task1 = CreateTestWorkItem("Task", "PBI1 Dev Task", "Done");
            var pbi1Task2 = CreateTestWorkItem("Task", "PBI1 QA Task", "Done");
            
            // Tasks for PBI 2
            var pbi2Task1 = CreateTestWorkItem("Task", "PBI2 Dev Task", "Done");
            
            // Task for Bug
            var bugTask1 = CreateTestWorkItem("Task", "Bug Fix Task", "Done");
            
            // Set all to PCLabs project
            var allWorkItems = new[] { epic, feature, pbi1, pbi2, bug1, pbi1Task1, pbi1Task2, pbi2Task1, bugTask1 };
            foreach (var wi in allWorkItems)
            {
                wi.SetField("System.TeamProject", "PCLabs");
            }
            
            // Set up completed work and activities
            var today = DateTime.Now;
            
            pbi1Task1.SetField("Microsoft.VSTS.Scheduling.CompletedWork", 10.0);
            pbi1Task1.SetField("Microsoft.VSTS.Common.Activity", "Development");
            pbi1Task1.SetField("System.ChangedDate", today);
            
            pbi1Task2.SetField("Microsoft.VSTS.Scheduling.CompletedWork", 3.0);
            pbi1Task2.SetField("Microsoft.VSTS.Common.Activity", "Testing");
            pbi1Task2.SetField("System.ChangedDate", today);
            
            pbi2Task1.SetField("Microsoft.VSTS.Scheduling.CompletedWork", 6.0);
            pbi2Task1.SetField("Microsoft.VSTS.Common.Activity", "Development");
            pbi2Task1.SetField("System.ChangedDate", today);
            
            bugTask1.SetField("Microsoft.VSTS.Scheduling.CompletedWork", 2.0);
            bugTask1.SetField("Microsoft.VSTS.Common.Activity", "Development");
            bugTask1.SetField("System.ChangedDate", today);
            
            // Set up relationships
            epic.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = feature.Id });
            feature.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = pbi1.Id });
            feature.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = pbi2.Id });
            feature.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = bug1.Id });
            pbi1.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = pbi1Task1.Id });
            pbi1.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = pbi1Task2.Id });
            pbi2.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = pbi2Task1.Id });
            bug1.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = bugTask1.Id });
            
            // Save all work items
            foreach (var wi in allWorkItems)
            {
                await MockClient.SaveWorkItem(wi);
            }
            
            MockClient.SavedWorkItems.Clear();
            
            // Act
            var result = await ExecuteScriptFromFileAsync(AGGREGATION_SCRIPT_PATH);
            
            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("Hierarchical aggregation completed");
            
            // Expected totals: PBI1=13h (10+3), PBI2=6h, Bug=2h, Feature=21h, Epic=21h
            var updatedFeature = MockClient.SavedWorkItems.FirstOrDefault(w => 
                w.GetField<string>("System.WorkItemType") == "Feature");
            var updatedEpic = MockClient.SavedWorkItems.FirstOrDefault(w => 
                w.GetField<string>("System.WorkItemType") == "Epic");
            
            if (updatedFeature != null)
            {
                updatedFeature.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork").Should().Be(21.0); // 13+6+2
                updatedFeature.GetField<double?>("Custom.DevelopmentCompletedWork").Should().Be(18.0); // 10+6+2
                updatedFeature.GetField<double?>("Custom.QACompletedWork").Should().Be(3.0);
            }
            
            if (updatedEpic != null)
            {
                updatedEpic.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork").Should().Be(21.0);
                updatedEpic.GetField<double?>("Custom.DevelopmentCompletedWork").Should().Be(18.0);
                updatedEpic.GetField<double?>("Custom.QACompletedWork").Should().Be(3.0);
            }
        }
        
        [Fact]
        public async Task HierarchicalAggregation_TopDown_FeatureEstimates_ShouldAggregateToEpic()
        {
            // Arrange - Create Epic with Features that have estimation/remaining work
            ClearTestData();
            
            var epic = CreateTestWorkItem("Epic", "Estimation Epic", "Active");
            var feature1 = CreateTestWorkItem("Feature", "Feature 1", "Active");
            var feature2 = CreateTestWorkItem("Feature", "Feature 2", "Active");
            
            // Set all to PCLabs project
            epic.SetField("System.TeamProject", "PCLabs");
            feature1.SetField("System.TeamProject", "PCLabs");
            feature2.SetField("System.TeamProject", "PCLabs");
            
            // Set up estimation and remaining work on features
            var today = DateTime.Now;
            
            // Feature 1 estimates/remaining
            feature1.SetField("Microsoft.VSTS.Scheduling.Effort", 40.0);
            feature1.SetField("Custom.DevelopmentEffortEstimation", 25.0);
            feature1.SetField("Custom.QAEffortEstimation", 10.0);
            feature1.SetField("Custom.POEffortEstimation", 5.0);
            feature1.SetField("Microsoft.VSTS.Scheduling.RemainingWork", 30.0);
            feature1.SetField("Custom.DevelopmentRemainingWork", 20.0);
            feature1.SetField("Custom.QARemainingWork", 8.0);
            feature1.SetField("Custom.PORemainingWork", 2.0);
            feature1.SetField("System.ChangedDate", today);
            
            // Feature 2 estimates/remaining
            feature2.SetField("Microsoft.VSTS.Scheduling.Effort", 60.0);
            feature2.SetField("Custom.DevelopmentEffortEstimation", 35.0);
            feature2.SetField("Custom.QAEffortEstimation", 15.0);
            feature2.SetField("Custom.POEffortEstimation", 10.0);
            feature2.SetField("Microsoft.VSTS.Scheduling.RemainingWork", 50.0);
            feature2.SetField("Custom.DevelopmentRemainingWork", 30.0);
            feature2.SetField("Custom.QARemainingWork", 12.0);
            feature2.SetField("Custom.PORemainingWork", 8.0);
            feature2.SetField("System.ChangedDate", today);
            
            // Set up relationships
            epic.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = feature1.Id });
            epic.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = feature2.Id });
            
            // Save work items
            await MockClient.SaveWorkItem(epic);
            await MockClient.SaveWorkItem(feature1);
            await MockClient.SaveWorkItem(feature2);
            
            MockClient.SavedWorkItems.Clear();
            
            // Act
            var result = await ExecuteScriptFromFileAsync(AGGREGATION_SCRIPT_PATH);
            
            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("changed features since last run");
            result.ShouldHaveLogMessageContaining("Finding affected epics using batched queries");
            
            // Verify Epic was updated with aggregated estimates
            var updatedEpic = MockClient.SavedWorkItems.FirstOrDefault(w => 
                w.GetField<string>("System.WorkItemType") == "Epic");
            
            updatedEpic.Should().NotBeNull();
            
            // Verify total effort estimation (40 + 60 = 100)
            updatedEpic.GetField<double?>("Microsoft.VSTS.Scheduling.Effort").Should().Be(100.0);
            updatedEpic.GetField<double?>("Custom.TotalEffortEstimation").Should().Be(100.0);
            
            // Verify discipline breakdowns for estimation
            updatedEpic.GetField<double?>("Custom.DevelopmentEffortEstimation").Should().Be(60.0); // 25 + 35
            updatedEpic.GetField<double?>("Custom.QAEffortEstimation").Should().Be(25.0); // 10 + 15
            updatedEpic.GetField<double?>("Custom.POEffortEstimation").Should().Be(15.0); // 5 + 10
            
            // Verify total remaining work (30 + 50 = 80)
            updatedEpic.GetField<double?>("Microsoft.VSTS.Scheduling.RemainingWork").Should().Be(80.0);
            
            // Verify discipline breakdowns for remaining work
            updatedEpic.GetField<double?>("Custom.DevelopmentRemainingWork").Should().Be(50.0); // 20 + 30
            updatedEpic.GetField<double?>("Custom.QARemainingWork").Should().Be(20.0); // 8 + 12
            updatedEpic.GetField<double?>("Custom.PORemainingWork").Should().Be(10.0); // 2 + 8
        }
        
        [Fact]
        public async Task HierarchicalAggregation_CombinedScenario_ShouldHandleBothDirections()
        {
            // Arrange - Create complex hierarchy that tests both bottom-up and top-down aggregation
            ClearTestData();
            
            var epic = CreateTestWorkItem("Epic", "Combined Epic", "Active");
            var feature1 = CreateTestWorkItem("Feature", "Feature with Tasks", "Active");
            var feature2 = CreateTestWorkItem("Feature", "Feature with Estimates", "Active");
            var pbi = CreateTestWorkItem("Product Backlog Item", "PBI with Tasks", "Active");
            var task1 = CreateTestWorkItem("Task", "Completed Task", "Done");
            var task2 = CreateTestWorkItem("Task", "Another Task", "Done");
            
            // Set all to PCLabs project
            var allItems = new[] { epic, feature1, feature2, pbi, task1, task2 };
            foreach (var item in allItems)
            {
                item.SetField("System.TeamProject", "PCLabs");
            }
            
            var today = DateTime.Now;
            
            // Set up completed work on tasks (for bottom-up aggregation)
            task1.SetField("Microsoft.VSTS.Scheduling.CompletedWork", 12.0);
            task1.SetField("Microsoft.VSTS.Common.Activity", "Development");
            task1.SetField("System.ChangedDate", today);
            
            task2.SetField("Microsoft.VSTS.Scheduling.CompletedWork", 8.0);
            task2.SetField("Microsoft.VSTS.Common.Activity", "Testing");
            task2.SetField("System.ChangedDate", today);
            
            // Set up estimates on feature2 (for top-down aggregation)
            feature2.SetField("Microsoft.VSTS.Scheduling.Effort", 50.0);
            feature2.SetField("Custom.DevelopmentEffortEstimation", 30.0);
            feature2.SetField("Custom.QAEffortEstimation", 15.0);
            feature2.SetField("Custom.POEffortEstimation", 5.0);
            feature2.SetField("System.ChangedDate", today);
            
            // Set up relationships
            epic.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = feature1.Id });
            epic.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = feature2.Id });
            feature1.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = pbi.Id });
            pbi.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = task1.Id });
            pbi.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = task2.Id });
            
            // Save all work items
            foreach (var item in allItems)
            {
                await MockClient.SaveWorkItem(item);
            }
            
            MockClient.SavedWorkItems.Clear();
            
            // Act
            var result = await ExecuteScriptFromFileAsync(AGGREGATION_SCRIPT_PATH);
            
            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("changed tasks with completed work since last run");
            result.ShouldHaveLogMessageContaining("changed features since last run");
            
            var updatedEpic = MockClient.SavedWorkItems.FirstOrDefault(w => 
                w.GetField<string>("System.WorkItemType") == "Epic");
            
            updatedEpic.Should().NotBeNull();
            
            // Verify both bottom-up completed work aggregation and top-down estimation aggregation
            updatedEpic.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork").Should().Be(20.0); // From tasks
            updatedEpic.GetField<double?>("Custom.DevelopmentCompletedWork").Should().Be(12.0);
            updatedEpic.GetField<double?>("Custom.QACompletedWork").Should().Be(8.0);
            
            updatedEpic.GetField<double?>("Microsoft.VSTS.Scheduling.Effort").Should().Be(50.0); // From feature estimates
            updatedEpic.GetField<double?>("Custom.DevelopmentEffortEstimation").Should().Be(30.0);
            updatedEpic.GetField<double?>("Custom.QAEffortEstimation").Should().Be(15.0);
            updatedEpic.GetField<double?>("Custom.POEffortEstimation").Should().Be(5.0);
        }
        
        [Fact]
        public async Task HierarchicalAggregation_NoChanges_ShouldExitEarly()
        {
            // Arrange - Create work items but don't set recent change dates
            ClearTestData();
            
            var epic = CreateTestWorkItem("Epic", "Unchanged Epic", "Active");
            var feature = CreateTestWorkItem("Feature", "Unchanged Feature", "Active");
            var pbi = CreateTestWorkItem("Product Backlog Item", "Unchanged PBI", "Active");
            var task = CreateTestWorkItem("Task", "Old Task", "Done");
            
            // Set all to PCLabs project
            epic.SetField("System.TeamProject", "PCLabs");
            feature.SetField("System.TeamProject", "PCLabs");
            pbi.SetField("System.TeamProject", "PCLabs");
            task.SetField("System.TeamProject", "PCLabs");
            
            // Set up completed work but with OLD change date (won't be picked up)
            task.SetField("Microsoft.VSTS.Scheduling.CompletedWork", 10.0);
            task.SetField("Microsoft.VSTS.Common.Activity", "Development");
            task.SetField("System.ChangedDate", DateTime.Now.AddDays(-5)); // 5 days ago
            
            // Set up relationships
            epic.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = feature.Id });
            feature.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = pbi.Id });
            pbi.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = task.Id });
            
            // Save work items
            await MockClient.SaveWorkItem(epic);
            await MockClient.SaveWorkItem(feature);
            await MockClient.SaveWorkItem(pbi);
            await MockClient.SaveWorkItem(task);
            
            MockClient.SavedWorkItems.Clear();
            
            // Act
            var result = await ExecuteScriptFromFileAsync(AGGREGATION_SCRIPT_PATH);
            
            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("No tasks or features with changes found - no aggregation needed");
            result.ShouldHaveResultMessage("No aggregation needed - next check in 10 minutes");
            
            // Verify no work items were updated
            MockClient.SavedWorkItems.Should().BeEmpty();
        }
        
        [Fact]
        public async Task HierarchicalAggregation_DisciplineMapping_ShouldMapActivitiesCorrectly()
        {
            // Arrange - Create tasks with various activities to test discipline mapping
            ClearTestData();
            
            var pbi = CreateTestWorkItem("Product Backlog Item", "Activity Test PBI", "Active");
            
            var tasks = new[]
            {
                (CreateTestWorkItem("Task", "Dev Task", "Done"), "Development", 8.0),
                (CreateTestWorkItem("Task", "QA Task", "Done"), "Testing", 4.0),
                (CreateTestWorkItem("Task", "Design Task", "Done"), "Design", 2.0),
                (CreateTestWorkItem("Task", "Admin Task", "Done"), "Admin Configuration", 1.0),
                (CreateTestWorkItem("Task", "Meeting Task", "Done"), "Ceremonies", 3.0),
                (CreateTestWorkItem("Task", "Unknown Task", "Done"), "Unknown Activity", 2.0) // Should go to Others
            };
            
            // Set all to PCLabs project
            pbi.SetField("System.TeamProject", "PCLabs");
            
            var today = DateTime.Now;
            foreach (var (task, activity, hours) in tasks)
            {
                task.SetField("System.TeamProject", "PCLabs");
                task.SetField("Microsoft.VSTS.Scheduling.CompletedWork", hours);
                task.SetField("Microsoft.VSTS.Common.Activity", activity);
                task.SetField("System.ChangedDate", today);
                
                pbi.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = task.Id });
            }
            
            // Save all work items
            await MockClient.SaveWorkItem(pbi);
            foreach (var (task, _, _) in tasks)
            {
                await MockClient.SaveWorkItem(task);
            }
            
            MockClient.SavedWorkItems.Clear();
            
            // Act
            var result = await ExecuteScriptFromFileAsync(AGGREGATION_SCRIPT_PATH);
            
            // Assert
            result.ShouldBeSuccessful();
            
            var updatedPBI = MockClient.SavedWorkItems.FirstOrDefault(w => 
                w.GetField<string>("System.WorkItemType") == "Product Backlog Item");
            
            updatedPBI.Should().NotBeNull();
            
            // Verify discipline mapping
            updatedPBI.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork").Should().Be(20.0); // Total
            updatedPBI.GetField<double?>("Custom.DevelopmentCompletedWork").Should().Be(8.0); // Development
            updatedPBI.GetField<double?>("Custom.QACompletedWork").Should().Be(4.0); // Testing -> QA
            updatedPBI.GetField<double?>("Custom.POCompletedWork").Should().Be(2.0); // Design -> PO
            updatedPBI.GetField<double?>("Custom.AdminCompletedWork").Should().Be(1.0); // Admin Configuration -> Admin
            updatedPBI.GetField<double?>("Custom.OthersCompletedWork").Should().Be(5.0); // Ceremonies + Unknown -> Others (3 + 2)
        }
    }
}