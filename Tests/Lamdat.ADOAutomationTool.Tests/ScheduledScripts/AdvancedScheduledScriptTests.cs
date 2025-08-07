using Lamdat.ADOAutomationTool.Tests.Framework;
using FluentAssertions;

namespace Lamdat.ADOAutomationTool.Tests.ScheduledScripts
{
    /// <summary>
    /// Advanced tests demonstrating complex scheduled script testing scenarios
    /// </summary>
    public class AdvancedScheduledScriptTests : ScheduledScriptTestBase
    {
        [Fact]
        public async Task BulkWorkItemProcessing_ShouldProcessAllItems()
        {
            // Arrange
            var workItems = CreateTestWorkItems(5, "Bug", "Test Bug");
            var script = @"
                Logger.Information(""Starting bulk processing..."");
                
                var queryParams = new QueryLinksByWiqlPrms
                {
                    Wiql = ""SELECT [System.Id] FROM WorkItems WHERE [System.WorkItemType] = 'Bug'""
                };
                
                var bugs = await Client.QuetyLinksByWiql(queryParams);
                Logger.Information($""Found {bugs.Count} bugs to process"");
                
                int processed = 0;
                foreach (var bug in bugs)
                {
                    bug.SetField(""System.State"", ""Active"");
                    bug.SetField(""Custom.ProcessedDate"", DateTime.UtcNow.ToString(""yyyy-MM-dd""));
                    await Client.SaveWorkItem(bug);
                    processed++;
                    Logger.Information($""Processed bug {bug.Id}"");
                }
                
                Logger.Information($""Bulk processing completed. Processed {processed} items."");
            ";

            // Act
            var result = await ExecuteScriptAsync(script);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("Starting bulk processing");
            result.ShouldHaveLogMessageContaining("Found 5 bugs to process");
            result.ShouldHaveLogMessageContaining("Bulk processing completed. Processed 5 items");
            
            MockClient.ShouldHaveExecutedQueries(1);
            MockClient.ShouldHaveSavedWorkItems(5);
            
            // Verify all bugs were updated
            var savedItems = MockClient.SavedWorkItems;
            savedItems.Should().HaveCount(5);
            savedItems.Should().OnlyContain(item => item.GetField<string>("System.State") == "Active");
            savedItems.Should().OnlyContain(item => !string.IsNullOrEmpty(item.GetField<string>("Custom.ProcessedDate")));
        }

        [Fact]
        public async Task ReportGeneration_ShouldCreateSummaryReport()
        {
            // Arrange
            CreateTestWorkItems(3, "Bug", "Active Bug");
            CreateTestWorkItems(2, "Task", "Completed Task");
            CreateTestWorkItems(1, "User Story", "In Progress Story");

            var script = @"
                Logger.Information(""Generating work item summary report..."");
                
                var allItemsQuery = new QueryLinksByWiqlPrms
                {
                    Wiql = ""SELECT [System.Id] FROM WorkItems""
                };
                
                var allItems = await Client.QuetyLinksByWiql(allItemsQuery);
                
                var summary = allItems
                    .GroupBy(item => item.GetField<string>(""System.WorkItemType""))
                    .ToDictionary(g => g.Key, g => g.Count());
                
                Logger.Information($""=== Work Item Summary Report ==="");
                Logger.Information($""Total Items: {allItems.Count}"");
                
                foreach (var kvp in summary)
                {
                    Logger.Information($""{kvp.Key}: {kvp.Value} items"");
                }
                
                Logger.Information($""Report generated at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC"");
            ";

            // Act
            var result = await ExecuteScriptAsync(script);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("Generating work item summary report");
            result.ShouldHaveLogMessageContaining("=== Work Item Summary Report ===");
            result.ShouldHaveLogMessageContaining("Total Items: 6");
            result.ShouldHaveLogMessageContaining("Bug: 3 items");
            result.ShouldHaveLogMessageContaining("Task: 2 items");
            result.ShouldHaveLogMessageContaining("User Story: 1 items");
            result.ShouldHaveLogMessageContaining("Report generated at:");
        }

        [Fact]
        public async Task IterationBasedProcessing_ShouldProcessCurrentSprintItems()
        {
            // Arrange
            var teamName = "Development Team";
            AddCurrentSprint(teamName, "Sprint 10");
            
            var currentSprintItems = CreateTestWorkItems(3, "Task", "Sprint Task");
            
            var script = $@"
                Logger.Information(""Processing current sprint items..."");
                
                var iterations = await Client.GetAllTeamIterations(""{teamName}"");
                var currentIteration = iterations.FirstOrDefault(i => i.StartDate <= DateTime.Now && i.EndDate >= DateTime.Now);
                
                if (currentIteration != null)
                {{
                    Logger.Information($""Current iteration: {{currentIteration.Name}}"");
                    
                    var sprintItemsQuery = new QueryLinksByWiqlPrms
                    {{
                        Wiql = ""SELECT [System.Id] FROM WorkItems WHERE [System.WorkItemType] = 'Task'""
                    }};
                    
                    var sprintItems = await Client.QuetyLinksByWiql(sprintItemsQuery);
                    Logger.Information($""Found {{sprintItems.Count}} tasks in current sprint"");
                    
                    foreach (var item in sprintItems)
                    {{
                        item.SetField(""System.IterationPath"", currentIteration.Name);
                        item.SetField(""Custom.SprintProcessed"", true);
                        await Client.SaveWorkItem(item);
                        Logger.Information($""Updated task {{item.Id}} for current sprint"");
                    }}
                }}
                else
                {{
                    Logger.Warning(""No current iteration found"");
                }}
            ";

            // Act
            var result = await ExecuteScriptAsync(script);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("Processing current sprint items");
            result.ShouldHaveLogMessageContaining("Current iteration: Sprint 10");
            result.ShouldHaveLogMessageContaining("Found 3 tasks in current sprint");
            
            MockClient.ShouldHaveSavedWorkItems(3);
            var savedItems = MockClient.SavedWorkItems;
            savedItems.Should().OnlyContain(item => item.GetField<string>("System.IterationPath") == "Sprint 10");
            savedItems.Should().OnlyContain(item => item.GetField<bool>("Custom.SprintProcessed") == true);
        }

        [Fact]
        public async Task ErrorRecovery_ShouldContinueAfterPartialFailure()
        {
            // Arrange
            CreateTestWorkItems(3, "Task", "Test Task");
            
            var script = @"
                Logger.Information(""Starting processing with error recovery..."");
                
                var queryParams = new QueryLinksByWiqlPrms
                {
                    Wiql = ""SELECT [System.Id] FROM WorkItems WHERE [System.WorkItemType] = 'Task'""
                };
                
                var tasks = await Client.QuetyLinksByWiql(queryParams);
                Logger.Information($""Processing {tasks.Count} tasks..."");
                
                int processed = 0;
                int errors = 0;
                
                foreach (var task in tasks)
                {
                    try
                    {
                        // Simulate an error condition on the second item
                        if (task.Id == tasks[1].Id)
                        {
                            throw new InvalidOperationException($""Simulated error for task {task.Id}"");
                        }
                        
                        task.SetField(""System.State"", ""Active"");
                        await Client.SaveWorkItem(task);
                        processed++;
                        Logger.Information($""Successfully processed task {task.Id}"");
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        Logger.Warning($""Error processing task {task.Id}: {ex.Message}"");
                    }
                }
                
                Logger.Information($""Processing completed. Success: {processed}, Errors: {errors}"");
            ";

            // Act
            var result = await ExecuteScriptAsync(script);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("Starting processing with error recovery");
            result.ShouldHaveLogMessageContaining("Processing 3 tasks");
            result.ShouldHaveLogMessageContaining("Simulated error for task");
            result.ShouldHaveLogMessageContaining("Processing completed. Success: 2, Errors: 1");
            
            MockClient.ShouldHaveSavedWorkItems(2); // Only 2 should be saved due to the simulated error
        }

        [Fact]
        public async Task PerformanceTest_ShouldCompleteWithinTimeLimit()
        {
            // Arrange
            CreateTestWorkItems(50, "Task", "Performance Test Task");
            
            var script = @"
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                Logger.Information(""Starting performance test..."");
                
                var queryParams = new QueryLinksByWiqlPrms
                {
                    Wiql = ""SELECT [System.Id] FROM WorkItems WHERE [System.WorkItemType] = 'Task'""
                };
                
                var tasks = await Client.QuetyLinksByWiql(queryParams);
                Logger.Information($""Processing {tasks.Count} tasks for performance test..."");
                
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = 5,
                    CancellationToken = cancellationToken
                };
                
                await Parallel.ForEachAsync(tasks, parallelOptions, async (task, ct) =>
                {
                    task.SetField(""Custom.ProcessedTimestamp"", DateTime.UtcNow.Ticks);
                    await Client.SaveWorkItem(task);
                });
                
                stopwatch.Stop();
                Logger.Information($""Performance test completed in {stopwatch.ElapsedMilliseconds}ms"");
            ";

            // Act
            var result = await ExecuteScriptAsync(script);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("Starting performance test");
            result.ShouldHaveLogMessageContaining("Processing 50 tasks for performance test");
            result.ShouldHaveLogMessageContaining("Performance test completed in");
            result.ShouldExecuteWithin(TimeSpan.FromSeconds(30)); // Should complete quickly with mock
            
            MockClient.ShouldHaveSavedWorkItems(50);
        }

        [Fact]
        public async Task ConditionalProcessing_ShouldProcessBasedOnConditions()
        {
            // Arrange
            var highPriorityItem = CreateTestWorkItem("Bug", "Critical Bug", "New");
            highPriorityItem.SetField("Microsoft.VSTS.Common.Priority", 1);
            await MockClient.SaveWorkItem(highPriorityItem);
            
            var lowPriorityItem = CreateTestWorkItem("Bug", "Minor Bug", "New");
            lowPriorityItem.SetField("Microsoft.VSTS.Common.Priority", 3);
            await MockClient.SaveWorkItem(lowPriorityItem);
            
            var script = @"
                Logger.Information(""Starting conditional processing..."");
                
                var queryParams = new QueryLinksByWiqlPrms
                {
                    Wiql = ""SELECT [System.Id] FROM WorkItems WHERE [System.WorkItemType] = 'Bug'""
                };
                
                var bugs = await Client.QuetyLinksByWiql(queryParams);
                Logger.Information($""Found {bugs.Count} bugs to evaluate"");
                
                int highPriorityProcessed = 0;
                int lowPrioritySkipped = 0;
                
                foreach (var bug in bugs)
                {
                    var priority = bug.GetField<int>(""Microsoft.VSTS.Common.Priority"");
                    
                    if (priority <= 2) // High priority
                    {
                        bug.SetField(""System.State"", ""Active"");
                        bug.SetField(""System.AssignedTo"", ""urgent-team@company.com"");
                        bug.SetField(""Custom.EscalatedDate"", DateTime.UtcNow.ToString(""yyyy-MM-dd""));
                        await Client.SaveWorkItem(bug);
                        highPriorityProcessed++;
                        Logger.Information($""Escalated high priority bug {bug.Id}"");
                    }
                    else
                    {
                        lowPrioritySkipped++;
                        Logger.Information($""Skipped low priority bug {bug.Id}"");
                    }
                }
                
                Logger.Information($""Conditional processing completed. High priority processed: {highPriorityProcessed}, Low priority skipped: {lowPrioritySkipped}"");
            ";

            // Act
            var result = await ExecuteScriptAsync(script);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("Starting conditional processing");
            result.ShouldHaveLogMessageContaining("Found 2 bugs to evaluate");
            result.ShouldHaveLogMessageContaining("Escalated high priority bug");
            result.ShouldHaveLogMessageContaining("Skipped low priority bug");
            result.ShouldHaveLogMessageContaining("High priority processed: 1, Low priority skipped: 1");
            
            MockClient.ShouldHaveSavedWorkItems(1); // Only high priority item should be saved
            var savedItem = MockClient.SavedWorkItems.Last();
            savedItem.ShouldHaveState("Active");
            savedItem.ShouldHaveField("System.AssignedTo", "urgent-team@company.com");
        }
    }
}