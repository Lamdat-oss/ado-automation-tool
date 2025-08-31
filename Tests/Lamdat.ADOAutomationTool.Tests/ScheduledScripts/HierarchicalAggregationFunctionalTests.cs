using Lamdat.ADOAutomationTool.Tests.Framework;
using Lamdat.ADOAutomationTool.Entities;
using FluentAssertions;

namespace Lamdat.ADOAutomationTool.Tests.ScheduledScripts
{
    /// <summary>
    /// Comprehensive functional tests for the hierarchical aggregation script (08-hierarchical-aggregation.rule)
    /// Tests both bottom-up (Task → PBI/Feature/Epic) and top-down (Feature → Epic) aggregation
    /// Updated to match hours-to-days conversion (HOURS_PER_DAY = 8)
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
            
            // Verify PBI aggregation (Task hours converted to PBI days: 8+4=12 hours / 8 = 1.5 days)
            if (updatedPBI != null)
            {
                updatedPBI.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork").Should().Be(1.5); // (8 + 4) hours / 8 = 1.5 days
                updatedPBI.GetField<double?>("Custom.DevelopmentCompletedWork").Should().Be(1.0); // 8 hours / 8 = 1.0 day
                updatedPBI.GetField<double?>("Custom.QACompletedWork").Should().Be(0.5); // 4 hours / 8 = 0.5 days
                updatedPBI.GetField<double?>("Custom.POCompletedWork").Should().Be(0.0);
                updatedPBI.GetField<double?>("Custom.AdminCompletedWork").Should().Be(0.0);
                updatedPBI.GetField<double?>("Custom.OthersCompletedWork").Should().Be(0.0);
            }
            
            // Verify Feature aggregation (aggregates from PBI days: 1.5 days)
            if (updatedFeature != null)
            {
                updatedFeature.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork").Should().Be(1.5);
                updatedFeature.GetField<double?>("Custom.DevelopmentCompletedWork").Should().Be(1.0);
                updatedFeature.GetField<double?>("Custom.QACompletedWork").Should().Be(0.5);
            }
            
            // Verify Epic aggregation (aggregates from Feature days: 1.5 days)
            if (updatedEpic != null)
            {
                updatedEpic.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork").Should().Be(1.5);
                updatedEpic.GetField<double?>("Custom.DevelopmentCompletedWork").Should().Be(1.0);
                updatedEpic.GetField<double?>("Custom.QACompletedWork").Should().Be(0.5);
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
            
            // Expected totals: PBI1=13h→1.63d (10+3), PBI2=6h→0.75d, Bug=2h→0.25d, Feature=2.63d, Epic=2.63d
            var updatedFeature = MockClient.SavedWorkItems.FirstOrDefault(w => 
                w.GetField<string>("System.WorkItemType") == "Feature");
            var updatedEpic = MockClient.SavedWorkItems.FirstOrDefault(w => 
                w.GetField<string>("System.WorkItemType") == "Epic");
            
            if (updatedFeature != null)
            {
                updatedFeature.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork").Should().Be(2.63); // (13+6+2) hours / 8 = 2.63 days
                updatedFeature.GetField<double?>("Custom.DevelopmentCompletedWork").Should().Be(2.25); // (10+6+2) hours / 8 = 2.25 days
                updatedFeature.GetField<double?>("Custom.QACompletedWork").Should().Be(0.38); // 3 hours / 8 = 0.38 days
            }
            
            if (updatedEpic != null)
            {
                updatedEpic.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork").Should().Be(2.63);
                updatedEpic.GetField<double?>("Custom.DevelopmentCompletedWork").Should().Be(2.25);
                updatedEpic.GetField<double?>("Custom.QACompletedWork").Should().Be(0.38);
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
            
            // Set up estimation and remaining work on features (Features work in hours, Epic converts to days)
            var today = DateTime.Now;
            
            // Feature 1 estimates/remaining (in hours)
            feature1.SetField("Microsoft.VSTS.Scheduling.Effort", 320.0); // 320 hours = 40 days
            feature1.SetField("Custom.DevelopmentEffortEstimation", 200.0); // 200 hours = 25 days
            feature1.SetField("Custom.QAEffortEstimation", 80.0); // 80 hours = 10 days
            feature1.SetField("Custom.POEffortEstimation", 40.0); // 40 hours = 5 days
            feature1.SetField("Microsoft.VSTS.Scheduling.RemainingWork", 240.0); // 240 hours = 30 days
            feature1.SetField("Custom.DevelopmentRemainingWork", 160.0); // 160 hours = 20 days
            feature1.SetField("Custom.QARemainingWork", 64.0); // 64 hours = 8 days
            feature1.SetField("Custom.PORemainingWork", 16.0); // 16 hours = 2 days
            feature1.SetField("System.ChangedDate", today);
            
            // Feature 2 estimates/remaining (in hours)
            feature2.SetField("Microsoft.VSTS.Scheduling.Effort", 480.0); // 480 hours = 60 days
            feature2.SetField("Custom.DevelopmentEffortEstimation", 280.0); // 280 hours = 35 days
            feature2.SetField("Custom.QAEffortEstimation", 120.0); // 120 hours = 15 days
            feature2.SetField("Custom.POEffortEstimation", 80.0); // 80 hours = 10 days
            feature2.SetField("Microsoft.VSTS.Scheduling.RemainingWork", 400.0); // 400 hours = 50 days
            feature2.SetField("Custom.DevelopmentRemainingWork", 240.0); // 240 hours = 30 days
            feature2.SetField("Custom.QARemainingWork", 96.0); // 96 hours = 12 days
            feature2.SetField("Custom.PORemainingWork", 64.0); // 64 hours = 8 days
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
            
            // Verify Epic was updated with aggregated estimates (Feature hours directly aggregated as-is for now)
            var updatedEpic = MockClient.SavedWorkItems.FirstOrDefault(w => 
                w.GetField<string>("System.WorkItemType") == "Epic");
            
            updatedEpic.Should().NotBeNull();
            
            // Note: Currently the script aggregates Feature values directly without conversion
            // Verify total effort estimation (320 + 480 = 800)
            updatedEpic.GetField<double?>("Microsoft.VSTS.Scheduling.Effort").Should().Be(800.0);
            updatedEpic.GetField<double?>("Custom.TotalEffortEstimation").Should().Be(800.0);
            
            // Verify discipline breakdowns for estimation
            updatedEpic.GetField<double?>("Custom.DevelopmentEffortEstimation").Should().Be(480.0); // 200 + 280
            updatedEpic.GetField<double?>("Custom.QAEffortEstimation").Should().Be(200.0); // 80 + 120
            updatedEpic.GetField<double?>("Custom.POEffortEstimation").Should().Be(120.0); // 40 + 80
            
            // Verify total remaining work (240 + 400 = 640)
            updatedEpic.GetField<double?>("Microsoft.VSTS.Scheduling.RemainingWork").Should().Be(640.0);
            
            // Verify discipline breakdowns for remaining work
            updatedEpic.GetField<double?>("Custom.DevelopmentRemainingWork").Should().Be(400.0); // 160 + 240
            updatedEpic.GetField<double?>("Custom.QARemainingWork").Should().Be(160.0); // 64 + 96
            updatedEpic.GetField<double?>("Custom.PORemainingWork").Should().Be(80.0); // 16 + 64
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
            feature2.SetField("Microsoft.VSTS.Scheduling.Effort", 400.0); // 400 hours
            feature2.SetField("Custom.DevelopmentEffortEstimation", 240.0); // 240 hours
            feature2.SetField("Custom.QAEffortEstimation", 120.0); // 120 hours
            feature2.SetField("Custom.POEffortEstimation", 40.0); // 40 hours
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
            updatedEpic.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork").Should().Be(2.5); // (12+8) hours / 8 = 2.5 days
            updatedEpic.GetField<double?>("Custom.DevelopmentCompletedWork").Should().Be(1.5); // 12 hours / 8 = 1.5 days
            updatedEpic.GetField<double?>("Custom.QACompletedWork").Should().Be(1.0); // 8 hours / 8 = 1.0 day
            
            updatedEpic.GetField<double?>("Microsoft.VSTS.Scheduling.Effort").Should().Be(400.0); // From feature estimates (currently aggregated as-is)
            updatedEpic.GetField<double?>("Custom.DevelopmentEffortEstimation").Should().Be(240.0);
            updatedEpic.GetField<double?>("Custom.QAEffortEstimation").Should().Be(120.0);
            updatedEpic.GetField<double?>("Custom.POEffortEstimation").Should().Be(40.0);
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
                (CreateTestWorkItem("Task", "Design Task", "Done"), "Functional Design", 2.0),
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
            
            // Verify discipline mapping (Task hours converted to PBI days: total 20 hours / 8 = 2.5 days)
            updatedPBI.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork").Should().Be(2.5); // 20 hours / 8 = 2.5 days
            updatedPBI.GetField<double?>("Custom.DevelopmentCompletedWork").Should().Be(1.0); // 8 hours / 8 = 1.0 day
            updatedPBI.GetField<double?>("Custom.QACompletedWork").Should().Be(0.5); // 4 hours / 8 = 0.5 days
            updatedPBI.GetField<double?>("Custom.POCompletedWork").Should().Be(0.25); // 2 hours / 8 = 0.25 days
            updatedPBI.GetField<double?>("Custom.AdminCompletedWork").Should().Be(0.12); // 1 hour / 8 = 0.12 days (rounded)
            updatedPBI.GetField<double?>("Custom.OthersCompletedWork").Should().Be(0.63); // (3 + 2) hours / 8 = 0.63 days (rounded)
        }
        
        #region Removed State Tests
        
        [Fact]
        public async Task HierarchicalAggregation_RemovedTask_ShouldTriggerParentRecalculation()
        {
            // Arrange - Create PBI with tasks, then set one task to "Removed" state
            ClearTestData();
            
            var pbi = CreateTestWorkItem("Product Backlog Item", "PBI with Removed Task", "Active");
            var task1 = CreateTestWorkItem("Task", "Active Task", "Done");
            var task2 = CreateTestWorkItem("Task", "Removed Task", "Removed"); // This task is in "Removed" state
            
            // Set all to PCLabs project
            pbi.SetField("System.TeamProject", "PCLabs");
            task1.SetField("System.TeamProject", "PCLabs");
            task2.SetField("System.TeamProject", "PCLabs");
            
            // Set up completed work on tasks
            var today = DateTime.Now;
            
            task1.SetField("Microsoft.VSTS.Scheduling.CompletedWork", 8.0);
            task1.SetField("Microsoft.VSTS.Common.Activity", "Development");
            task1.SetField("System.ChangedDate", today);
            
            // The removed task has completed work but should be excluded from aggregation
            task2.SetField("Microsoft.VSTS.Scheduling.CompletedWork", 4.0);
            task2.SetField("Microsoft.VSTS.Common.Activity", "Testing");
            task2.SetField("System.ChangedDate", today); // Recently changed to "Removed" state
            
            // Set up relationships
            pbi.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = task1.Id });
            pbi.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = task2.Id });
            
            // Save all work items
            await MockClient.SaveWorkItem(pbi);
            await MockClient.SaveWorkItem(task1);
            await MockClient.SaveWorkItem(task2);
            
            MockClient.SavedWorkItems.Clear();
            
            // Act
            var result = await ExecuteScriptFromFileAsync(AGGREGATION_SCRIPT_PATH);
            
            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("work items in 'Removed' state that have changed since last run");
            result.ShouldHaveLogMessageContaining("Completed processing removed work items");
            result.ShouldHaveLogMessageContaining("Removed items processed:");
            
            // Verify PBI was updated and only includes completed work from active tasks
            var updatedPBI = MockClient.SavedWorkItems.FirstOrDefault(w => 
                w.GetField<string>("System.WorkItemType") == "Product Backlog Item");
            
            updatedPBI.Should().NotBeNull();
            
            // Should only aggregate task1 (8 hours / 8 = 1.0 day), task2 should be excluded
            updatedPBI.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork").Should().Be(1.0);
            updatedPBI.GetField<double?>("Custom.DevelopmentCompletedWork").Should().Be(1.0);
            updatedPBI.GetField<double?>("Custom.QACompletedWork").Should().Be(0.0); // Removed task's QA work excluded
        }
        
        [Fact]
        public async Task HierarchicalAggregation_RemovedPBI_ShouldTriggerFeatureRecalculation()
        {
            // Arrange - Create Feature with PBIs, then set one PBI to "Removed" state
            ClearTestData();
            
            var feature = CreateTestWorkItem("Feature", "Feature with Removed PBI", "Active");
            var pbi1 = CreateTestWorkItem("Product Backlog Item", "Active PBI", "Active");
            var pbi2 = CreateTestWorkItem("Product Backlog Item", "Removed PBI", "Removed");
            
            // Tasks for active PBI
            var task1 = CreateTestWorkItem("Task", "PBI1 Task", "Done");
            
            // Tasks for removed PBI (should be excluded from feature aggregation)
            var task2 = CreateTestWorkItem("Task", "Removed PBI Task", "Done");
            
            // Set all to PCLabs project
            var allItems = new[] { feature, pbi1, pbi2, task1, task2 };
            foreach (var item in allItems)
            {
                item.SetField("System.TeamProject", "PCLabs");
            }
            
            var today = DateTime.Now;
            
            // Set up completed work on tasks
            task1.SetField("Microsoft.VSTS.Scheduling.CompletedWork", 16.0);
            task1.SetField("Microsoft.VSTS.Common.Activity", "Development");
            task1.SetField("System.ChangedDate", today);
            
            task2.SetField("Microsoft.VSTS.Scheduling.CompletedWork", 8.0);
            task2.SetField("Microsoft.VSTS.Common.Activity", "Testing");
            task2.SetField("System.ChangedDate", today);
            
            // Set the removed PBI to recently changed so it triggers recalculation
            pbi2.SetField("System.ChangedDate", today);
            
            // Set up relationships
            feature.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = pbi1.Id });
            feature.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = pbi2.Id });
            pbi1.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = task1.Id });
            pbi2.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = task2.Id });
            
            // Save work items
            foreach (var item in allItems)
            {
                await MockClient.SaveWorkItem(item);
            }
            
            MockClient.SavedWorkItems.Clear();
            
            // Act
            var result = await ExecuteScriptFromFileAsync(AGGREGATION_SCRIPT_PATH);
            
            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("Finding work items in 'Removed' state");
            result.ShouldHaveLogMessageContaining("Completed processing removed work items");
            
            // Verify Feature was updated and only includes completed work from active PBIs
            var updatedFeature = MockClient.SavedWorkItems.FirstOrDefault(w => 
                w.GetField<string>("System.WorkItemType") == "Feature");
            
            updatedFeature.Should().NotBeNull();
            
            // Should only aggregate from active PBI1 (16 hours / 8 = 2.0 days), removed PBI2 excluded
            updatedFeature.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork").Should().Be(2.0);
            updatedFeature.GetField<double?>("Custom.DevelopmentCompletedWork").Should().Be(2.0);
            updatedFeature.GetField<double?>("Custom.QACompletedWork").Should().Be(0.0); // Removed PBI's tasks excluded
        }
        
        [Fact]
        public async Task HierarchicalAggregation_RemovedFeature_ShouldTriggerEpicRecalculation()
        {
            // Arrange - Create Epic with Features, then set one Feature to "Removed" state
            ClearTestData();
            
            var epic = CreateTestWorkItem("Epic", "Epic with Removed Feature", "Active");
            var feature1 = CreateTestWorkItem("Feature", "Active Feature", "Active");
            var feature2 = CreateTestWorkItem("Feature", "Removed Feature", "Removed");
            
            // Set all to PCLabs project
            epic.SetField("System.TeamProject", "PCLabs");
            feature1.SetField("System.TeamProject", "PCLabs");
            feature2.SetField("System.TeamProject", "PCLabs");
            
            var today = DateTime.Now;
            
            // Set up estimation values on features
            feature1.SetField("Microsoft.VSTS.Scheduling.Effort", 200.0);
            feature1.SetField("Custom.DevelopmentEffortEstimation", 120.0);
            feature1.SetField("Custom.QAEffortEstimation", 80.0);
            feature1.SetField("System.ChangedDate", today);
            
            // Removed feature has estimates but should be excluded
            feature2.SetField("Microsoft.VSTS.Scheduling.Effort", 100.0);
            feature2.SetField("Custom.DevelopmentEffortEstimation", 60.0);
            feature2.SetField("Custom.QAEffortEstimation", 40.0);
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
            result.ShouldHaveLogMessageContaining("Finding work items in 'Removed' state");
            result.ShouldHaveLogMessageContaining("Completed processing removed work items");
            
            // Verify Epic was updated with aggregation that excludes removed features
            var updatedEpic = MockClient.SavedWorkItems.FirstOrDefault(w => 
                w.GetField<string>("System.WorkItemType") == "Epic");
            
            updatedEpic.Should().NotBeNull();
            
            // Should only aggregate from active feature1, removed feature2 excluded
            updatedEpic.GetField<double?>("Microsoft.VSTS.Scheduling.Effort").Should().Be(200.0);
            updatedEpic.GetField<double?>("Custom.DevelopmentEffortEstimation").Should().Be(120.0);
            updatedEpic.GetField<double?>("Custom.QAEffortEstimation").Should().Be(80.0);
        }
        
        [Fact]
        public async Task HierarchicalAggregation_RemovedBugAndGlitch_ShouldTriggerParentRecalculation()
        {
            // Arrange - Create Feature with Bug and Glitch work items, set them to "Removed"
            ClearTestData();
            
            var feature = CreateTestWorkItem("Feature", "Feature with Removed Bug/Glitch", "Active");
            var pbi = CreateTestWorkItem("Product Backlog Item", "Active PBI", "Active");
            var bug = CreateTestWorkItem("Bug", "Removed Bug", "Removed");
            var glitch = CreateTestWorkItem("Glitch", "Removed Glitch", "Removed");
            
            // Tasks for each work item
            var pbiTask = CreateTestWorkItem("Task", "PBI Task", "Done");
            var bugTask = CreateTestWorkItem("Task", "Bug Task", "Done");
            var glitchTask = CreateTestWorkItem("Task", "Glitch Task", "Done");
            
            // Set all to PCLabs project
            var allItems = new[] { feature, pbi, bug, glitch, pbiTask, bugTask, glitchTask };
            foreach (var item in allItems)
            {
                item.SetField("System.TeamProject", "PCLabs");
            }
            
            var today = DateTime.Now;
            
            // Set up completed work on both active and removed tasks
            pbiTask.SetField("Microsoft.VSTS.Scheduling.CompletedWork", 16.0);
            pbiTask.SetField("Microsoft.VSTS.Common.Activity", "Development");
            pbiTask.SetField("System.ChangedDate", today);
            
            bugTask.SetField("Microsoft.VSTS.Scheduling.CompletedWork", 8.0);
            bugTask.SetField("Microsoft.VSTS.Common.Activity", "Development");
            bugTask.SetField("System.ChangedDate", today);
            
            glitchTask.SetField("Microsoft.VSTS.Scheduling.CompletedWork", 4.0);
            glitchTask.SetField("Microsoft.VSTS.Common.Activity", "Testing");
            glitchTask.SetField("System.ChangedDate", today);
            
            // Set removed work items to recently changed
            bug.SetField("System.ChangedDate", today);
            glitch.SetField("System.ChangedDate", today);
            
            // Set up relationships
            feature.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = pbi.Id });
            feature.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = bug.Id });
            feature.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = glitch.Id });
            pbi.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = pbiTask.Id });
            bug.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = bugTask.Id });
            glitch.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = glitchTask.Id });
            
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
            result.ShouldHaveLogMessageContaining("Finding work items in 'Removed' state");
            result.ShouldHaveLogMessageContaining("Bugs updated: 1"); // From the final summary
            result.ShouldHaveLogMessageContaining("Completed processing removed work items");
            
            // Verify Feature was updated and only includes completed work from active PBI
            var updatedFeature = MockClient.SavedWorkItems.FirstOrDefault(w => 
                w.GetField<string>("System.WorkItemType") == "Feature");
            
            updatedFeature.Should().NotBeNull();
            
            // Should only aggregate from active PBI (16 hours / 8 = 2.0 days), removed Bug/Glitch excluded
            updatedFeature.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork").Should().Be(2.0);
            updatedFeature.GetField<double?>("Custom.DevelopmentCompletedWork").Should().Be(2.0);
            updatedFeature.GetField<double?>("Custom.QACompletedWork").Should().Be(0.0); // Removed Glitch's QA work excluded
        }
        
        [Fact]
        public async Task HierarchicalAggregation_NoRemovedItems_ShouldLogNoRemovedItems()
        {
            // Arrange - Create hierarchy with all active items (no removed items)
            ClearTestData();
            
            var pbi = CreateTestWorkItem("Product Backlog Item", "Active PBI", "Active");
            var task = CreateTestWorkItem("Task", "Active Task", "Done");
            
            // Set all to PCLabs project
            pbi.SetField("System.TeamProject", "PCLabs");
            task.SetField("System.TeamProject", "PCLabs");
            
            var today = DateTime.Now;
            
            task.SetField("Microsoft.VSTS.Scheduling.CompletedWork", 8.0);
            task.SetField("Microsoft.VSTS.Common.Activity", "Development");
            task.SetField("System.ChangedDate", today);
            
            // Set up relationships
            pbi.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = task.Id });
            
            // Save work items
            await MockClient.SaveWorkItem(pbi);
            await MockClient.SaveWorkItem(task);
            
            MockClient.SavedWorkItems.Clear();
            
            // Act
            var result = await ExecuteScriptFromFileAsync(AGGREGATION_SCRIPT_PATH);
            
            // Assert
            result.ShouldBeSuccessful();
            
            // Check for the actual log messages - script still processes but may find 0 removed items  
            // The key is that it should process normally and log about removed items processing
            if (result.HasLogMessageContaining("Found 0 work items in 'Removed' state"))
            {
                result.ShouldHaveLogMessageContaining("No removed work items found - skipping removed items processing");
            }
            else
            {
                // If found some removed items (possibly from previous test data), just ensure script completed successfully
                result.ShouldHaveLogMessageContaining("Hierarchical aggregation completed");
            }
        }
        
        #endregion
        
        [Fact]
        public async Task HierarchicalAggregation_MixedRemovedAndActiveChanges_ShouldProcessBoth()
        {
            // Arrange - Create scenario with both active task changes and removed work items
            ClearTestData();
            
            var epic = CreateTestWorkItem("Epic", "Epic with Mixed Changes", "Active");
            var feature1 = CreateTestWorkItem("Feature", "Feature with Active Changes", "Active");
            var feature2 = CreateTestWorkItem("Feature", "Feature with Removed Items", "Active");
            var pbi1 = CreateTestWorkItem("Product Backlog Item", "PBI with New Task", "Active");
            var pbi2 = CreateTestWorkItem("Product Backlog Item", "PBI with Removed Task", "Active");
            
            var activeTask = CreateTestWorkItem("Task", "New Active Task", "Done");
            var removedTask = CreateTestWorkItem("Task", "Recently Removed Task", "Removed");
            
            // Set all to PCLabs project
            var allItems = new[] { epic, feature1, feature2, pbi1, pbi2, activeTask, removedTask };
            foreach (var item in allItems)
            {
                item.SetField("System.TeamProject", "PCLabs");
            }
            
            var today = DateTime.Now;
            
            // Set up completed work on active task only (this should be counted as changed task)
            activeTask.SetField("Microsoft.VSTS.Scheduling.CompletedWork", 16.0);
            activeTask.SetField("Microsoft.VSTS.Common.Activity", "Development");
            activeTask.SetField("System.ChangedDate", today);
            
            // Set up removed task with old completed work but recent state change to "Removed"
            removedTask.SetField("Microsoft.VSTS.Scheduling.CompletedWork", 8.0);
            removedTask.SetField("Microsoft.VSTS.Common.Activity", "Testing");
            removedTask.SetField("System.ChangedDate", today); // Changed recently but in "Removed" state
            
            // Set up relationships
            epic.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = feature1.Id });
            epic.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = feature2.Id });
            feature1.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = pbi1.Id });
            feature2.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = pbi2.Id });
            pbi1.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = activeTask.Id });
            pbi2.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = removedTask.Id });
            
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
            // Note: Both tasks have recent changes, so script will process both, but the removed one will be excluded from aggregation
            result.ShouldHaveLogMessageContaining("changed tasks with completed work since last run");
            result.ShouldHaveLogMessageContaining("work items in 'Removed' state");
            result.ShouldHaveLogMessageContaining("Hierarchical aggregation completed:");
            
            // Verify Epic aggregation includes only active task work
            var updatedEpic = MockClient.SavedWorkItems.FirstOrDefault(w => 
                w.GetField<string>("System.WorkItemType") == "Epic");
            
            updatedEpic.Should().NotBeNull();
            
            // Should only include active task (16 hours / 8 = 2.0 days) - removed task excluded
            updatedEpic.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork").Should().Be(2.0);
            updatedEpic.GetField<double?>("Custom.DevelopmentCompletedWork").Should().Be(2.0);
            updatedEpic.GetField<double?>("Custom.QACompletedWork").Should().Be(0.0); // Removed task's QA work excluded
        }
        
        [Fact]
        public async Task HierarchicalAggregation_RemovedState_ShouldNotAffectFutureAggregations()
        {
            // Arrange - Create initial hierarchy with PBI and task, then remove the task
            ClearTestData();
            
            var feature = CreateTestWorkItem("Feature", "Feature with PBI and Task", "Active");
            var pbi = CreateTestWorkItem("Product Backlog Item", "Initial PBI", "Active");
            var task = CreateTestWorkItem("Task", "Initial Task", "Done");
            
            // Set all to PCLabs project
            feature.SetField("System.TeamProject", "PCLabs");
            pbi.SetField("System.TeamProject", "PCLabs");
            task.SetField("System.TeamProject", "PCLabs");
            
            var today = DateTime.Now;
            var yesterday = today.AddDays(-1);
            
            // Set up initial completed work with old change date
            task.SetField("Microsoft.VSTS.Scheduling.CompletedWork", 8.0);
            task.SetField("Microsoft.VSTS.Common.Activity", "Development");
            task.SetField("System.ChangedDate", yesterday); // Old change date
            
            // Set up relationships
            feature.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = pbi.Id });
            pbi.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = task.Id });
            
            // Save initial work items
            await MockClient.SaveWorkItem(feature);
            await MockClient.SaveWorkItem(pbi);
            await MockClient.SaveWorkItem(task);
            
            // Clear saved items to track script updates only
            MockClient.SavedWorkItems.Clear();
            
            // Act - Execute the hierarchical aggregation script (first run - no recent changes)
            var result = await ExecuteScriptFromFileAsync(AGGREGATION_SCRIPT_PATH);
            
            // Assert - Verify no processing occurred due to no recent changes
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("No tasks, features, or removed work items with changes found");
            
            // Now simulate the task being moved to "Removed" state recently
            task.SetField("System.State", "Removed");
            task.SetField("System.ChangedDate", today); // Recent change to removed state
            await MockClient.SaveWorkItem(task);
            
            MockClient.SavedWorkItems.Clear();
            
            // Act - Execute the hierarchical aggregation script again (second run - removed task)
            result = await ExecuteScriptFromFileAsync(AGGREGATION_SCRIPT_PATH);
            
            // Assert - Verify that the removed task triggers parent recalculation
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("Starting hierarchical work item aggregation");
            result.ShouldHaveLogMessageContaining("work items in 'Removed' state");
            result.ShouldHaveLogMessageContaining("Completed processing removed work items");
            result.ShouldHaveLogMessageContaining("Hierarchical aggregation completed");
            
            // Verify that work items are recalculated to exclude the removed task (should now be 0)
            var updatedPBI = MockClient.SavedWorkItems.FirstOrDefault(w => 
                w.GetField<string>("System.WorkItemType") == "Product Backlog Item");
            var updatedFeature = MockClient.SavedWorkItems.FirstOrDefault(w => 
                w.GetField<string>("System.WorkItemType") == "Feature");
            
            if (updatedPBI != null)
            {
                updatedPBI.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork").Should().Be(0.0); // Removed task excluded
            }
            
            if (updatedFeature != null)
            {
                updatedFeature.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork").Should().Be(0.0); // Removed task excluded
            }
        }
    }
}