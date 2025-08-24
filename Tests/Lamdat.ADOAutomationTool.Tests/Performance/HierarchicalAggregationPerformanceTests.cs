using Lamdat.ADOAutomationTool.Tests.Framework;
using Lamdat.ADOAutomationTool.Entities;
using Lamdat.ADOAutomationTool.Service;
using FluentAssertions;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Lamdat.ADOAutomationTool.Tests.Performance
{
    /// <summary>
    /// Performance tests for the hierarchical aggregation script against real Azure DevOps
    /// These tests require actual Azure DevOps credentials and should be run manually for performance analysis
    /// </summary>
    [Trait("Category", "Performance")]
    [Trait("Category", "Integration")]
    public class HierarchicalAggregationPerformanceTests : IDisposable
    {
        private readonly IAzureDevOpsClient _realClient;
        private readonly ILogger<HierarchicalAggregationPerformanceTests> _logger;
        private readonly string _testProject;
        private readonly List<int> _createdWorkItemIds = new();
        private readonly bool _skipCleanup;
        private static string AGGREGATION_SCRIPT_PATH
        {
            get
            {
                var currentDirectory = Directory.GetCurrentDirectory();
                var solutionDirectory = currentDirectory;
                
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

        public HierarchicalAggregationPerformanceTests()
        {
            // Load configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .AddUserSecrets<HierarchicalAggregationPerformanceTests>(optional: true)
                .AddEnvironmentVariables()
                .Build();

            // Setup logging
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
            var serviceProvider = serviceCollection.BuildServiceProvider();
            var microsoftLogger = serviceProvider.GetRequiredService<ILogger<HierarchicalAggregationPerformanceTests>>();
            _logger = microsoftLogger;

            // Create Serilog logger for Azure DevOps client
            var serilogLogger = new LoggerConfiguration()
                .WriteTo.Console()
                .CreateLogger();

            // Get Azure DevOps settings
            var azureDevOpsUrl = configuration["AzureDevOps:Url"] ?? 
                Environment.GetEnvironmentVariable("AZURE_DEVOPS_URL") ??
                throw new InvalidOperationException("Azure DevOps URL not configured. Set AzureDevOps:Url in appsettings.json or AZURE_DEVOPS_URL environment variable.");

            var personalAccessToken = configuration["AzureDevOps:PersonalAccessToken"] ?? 
                Environment.GetEnvironmentVariable("AZURE_DEVOPS_PAT") ??
                throw new InvalidOperationException("Azure DevOps PAT not configured. Set AzureDevOps:PersonalAccessToken in appsettings.json or AZURE_DEVOPS_PAT environment variable.");

            _testProject = configuration["AzureDevOps:TestProject"] ?? 
                Environment.GetEnvironmentVariable("AZURE_DEVOPS_TEST_PROJECT") ?? 
                "PCLabs"; // Default to PCLabs as per script

            _skipCleanup = bool.Parse(configuration["PerformanceTests:SkipCleanup"] ?? 
                Environment.GetEnvironmentVariable("SKIP_CLEANUP") ?? "false");

            // Create real Azure DevOps client with correct constructor parameters
            _realClient = new AzureDevOpsClient(serilogLogger, azureDevOpsUrl, personalAccessToken, false, false);
            _realClient.Project = _testProject;

            _logger.LogInformation("Performance tests initialized for project: {Project}", _testProject);
            _logger.LogInformation("Skip cleanup: {SkipCleanup}", _skipCleanup);
        }

        [Fact(Skip = "Performance test - run manually with real Azure DevOps credentials")]
        public async Task HierarchicalAggregation_SmallHierarchy_PerformanceBaseline()
        {
            // Arrange - Create small hierarchy for baseline performance
            _logger.LogInformation("=== SMALL HIERARCHY PERFORMANCE TEST ===");
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Create 1 Epic -> 2 Features -> 4 PBIs -> 12 Tasks
                var testData = await CreateSmallTestHierarchy();
                var setupTime = stopwatch.Elapsed;
                _logger.LogInformation("Test data setup completed in {SetupTime:F2}ms", setupTime.TotalMilliseconds);

                // Act - Execute the script
                stopwatch.Restart();
                var result = await ExecuteHierarchicalAggregationScript();
                var executionTime = stopwatch.Elapsed;

                // Assert & Log Results
                result.Should().BeTrue("Script should execute successfully");
                
                _logger.LogInformation("=== SMALL HIERARCHY PERFORMANCE RESULTS ===");
                _logger.LogInformation("Setup Time: {SetupTime:F2}ms", setupTime.TotalMilliseconds);
                _logger.LogInformation("Execution Time: {ExecutionTime:F2}ms", executionTime.TotalMilliseconds);
                _logger.LogInformation("Total Test Time: {TotalTime:F2}ms", (setupTime + executionTime).TotalMilliseconds);
                _logger.LogInformation("Work Items Created: {WorkItemCount}", testData.TotalWorkItems);
                _logger.LogInformation("Avg Time per Work Item: {AvgTime:F2}ms", executionTime.TotalMilliseconds / testData.TotalWorkItems);

                // Performance assertions
                executionTime.Should().BeLessThan(TimeSpan.FromSeconds(30), "Small hierarchy should complete within 30 seconds");
            }
            finally
            {
                await CleanupTestData();
            }
        }

        [Fact(Skip = "Performance test - run manually with real Azure DevOps credentials")]
        public async Task HierarchicalAggregation_MediumHierarchy_PerformanceTest()
        {
            // Arrange - Create medium hierarchy to test scalability
            _logger.LogInformation("=== MEDIUM HIERARCHY PERFORMANCE TEST ===");
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Create 2 Epics -> 6 Features -> 20 PBIs -> 60 Tasks
                var testData = await CreateMediumTestHierarchy();
                var setupTime = stopwatch.Elapsed;
                _logger.LogInformation("Test data setup completed in {SetupTime:F2}ms", setupTime.TotalMilliseconds);

                // Act - Execute the script
                stopwatch.Restart();
                var result = await ExecuteHierarchicalAggregationScript();
                var executionTime = stopwatch.Elapsed;

                // Assert & Log Results
                result.Should().BeTrue("Script should execute successfully");
                
                _logger.LogInformation("=== MEDIUM HIERARCHY PERFORMANCE RESULTS ===");
                _logger.LogInformation("Setup Time: {SetupTime:F2}ms", setupTime.TotalMilliseconds);
                _logger.LogInformation("Execution Time: {ExecutionTime:F2}ms", executionTime.TotalMilliseconds);
                _logger.LogInformation("Total Test Time: {TotalTime:F2}ms", (setupTime + executionTime).TotalMilliseconds);
                _logger.LogInformation("Work Items Created: {WorkItemCount}", testData.TotalWorkItems);
                _logger.LogInformation("Avg Time per Work Item: {AvgTime:F2}ms", executionTime.TotalMilliseconds / testData.TotalWorkItems);

                // Performance assertions
                executionTime.Should().BeLessThan(TimeSpan.FromMinutes(2), "Medium hierarchy should complete within 2 minutes");
            }
            finally
            {
                await CleanupTestData();
            }   
        }

        [Fact(Skip = "Performance test - run manually with real Azure DevOps credentials")]
        public async Task HierarchicalAggregation_LargeHierarchy_StressTest()
        {
            // Arrange - Create large hierarchy to test limits
            _logger.LogInformation("=== LARGE HIERARCHY STRESS TEST ===");
            var stopwatch = Stopwatch.StartNew();   

            try
            {
                // Create 5 Epics -> 15 Features -> 75 PBIs -> 300 Tasks
                var testData = await CreateLargeTestHierarchy();
                var setupTime = stopwatch.Elapsed;
                _logger.LogInformation("Test data setup completed in {SetupTime:F2}ms", setupTime.TotalMilliseconds);

                // Act - Execute the script with timeout
                stopwatch.Restart();
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                var result = await ExecuteHierarchicalAggregationScript(cts.Token);
                var executionTime = stopwatch.Elapsed;

                // Assert & Log Results
                result.Should().BeTrue("Script should execute successfully even with large hierarchy");
                
                _logger.LogInformation("=== LARGE HIERARCHY STRESS TEST RESULTS ===");
                _logger.LogInformation("Setup Time: {SetupTime:F2}ms", setupTime.TotalMilliseconds);
                _logger.LogInformation("Execution Time: {ExecutionTime:F2}ms", executionTime.TotalMilliseconds);
                _logger.LogInformation("Total Test Time: {TotalTime:F2}ms", (setupTime + executionTime).TotalMilliseconds);
                _logger.LogInformation("Work Items Created: {WorkItemCount}", testData.TotalWorkItems);
                _logger.LogInformation("Avg Time per Work Item: {AvgTime:F2}ms", executionTime.TotalMilliseconds / testData.TotalWorkItems);

                // Performance assertions
                executionTime.Should().BeLessThan(TimeSpan.FromMinutes(10), "Large hierarchy should complete within 10 minutes");
            }
            finally
            {
                await CleanupTestData();
            }
        }

        [Fact(Skip = "Performance test - run manually with real Azure DevOps credentials")]
        public async Task HierarchicalAggregation_NoChanges_PerformanceOptimization()
        {
            // Test that script exits quickly when no recent changes are found
            _logger.LogInformation("=== NO CHANGES OPTIMIZATION TEST ===");
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Create test data but with old change dates
                await CreateTestDataWithOldChangeDates();
                var setupTime = stopwatch.Elapsed;

                // Act - Execute the script (should exit early)
                stopwatch.Restart();
                var result = await ExecuteHierarchicalAggregationScript();
                var executionTime = stopwatch.Elapsed;

                // Assert & Log Results
                result.Should().BeTrue("Script should execute successfully and exit early");
                
                _logger.LogInformation("=== NO CHANGES OPTIMIZATION RESULTS ===");
                _logger.LogInformation("Setup Time: {SetupTime:F2}ms", setupTime.TotalMilliseconds);
                _logger.LogInformation("Execution Time: {ExecutionTime:F2}ms", executionTime.TotalMilliseconds);
                _logger.LogInformation("Early exit optimization working: {IsOptimized}", executionTime.TotalMilliseconds < 5000);

                // Performance assertion - should be very fast
                executionTime.Should().BeLessThan(TimeSpan.FromSeconds(10), "No changes scenario should exit quickly");
            }
            finally
            {
                await CleanupTestData();
            }
        }

        [Fact(Skip = "Performance test - run manually with real Azure DevOps credentials")]
        public async Task HierarchicalAggregation_ConcurrentExecution_SafetyTest()
        {
            // Test script behavior under concurrent execution
            _logger.LogInformation("=== CONCURRENT EXECUTION SAFETY TEST ===");
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Create test data
                var testData = await CreateSmallTestHierarchy();
                var setupTime = stopwatch.Elapsed;

                // Act - Execute script multiple times concurrently
                stopwatch.Restart();
                var tasks = new List<Task<bool>>();
                for (int i = 0; i < 3; i++)
                {
                    tasks.Add(ExecuteHierarchicalAggregationScript());
                }

                var results = await Task.WhenAll(tasks);
                var executionTime = stopwatch.Elapsed;

                // Assert & Log Results
                results.Should().AllSatisfy(r => r.Should().BeTrue("All concurrent executions should succeed"));
                
                _logger.LogInformation("=== CONCURRENT EXECUTION RESULTS ===");
                _logger.LogInformation("Setup Time: {SetupTime:F2}ms", setupTime.TotalMilliseconds);
                _logger.LogInformation("Concurrent Execution Time: {ExecutionTime:F2}ms", executionTime.TotalMilliseconds);
                _logger.LogInformation("Concurrent Executions: {Count}", tasks.Count);
                _logger.LogInformation("All executions successful: {AllSuccessful}", results.All(r => r));

                // Performance assertion
                executionTime.Should().BeLessThan(TimeSpan.FromMinutes(2), "Concurrent executions should complete within reasonable time");
            }
            finally
            {
                await CleanupTestData();
            }
        }

        private async Task<TestHierarchyData> CreateSmallTestHierarchy()
        {
            _logger.LogInformation("Creating small test hierarchy...");
            var data = new TestHierarchyData();

            // Create 1 Epic
            var epic = await CreateWorkItem("Epic", "Performance Test Epic");
            data.Epics.Add(epic);

            // Create 2 Features under Epic
            for (int f = 1; f <= 2; f++)
            {
                var feature = await CreateWorkItem("Feature", $"Performance Test Feature {f}");
                await CreateWorkItemLink(epic.Id, feature.Id);
                data.Features.Add(feature);

                // Create 2 PBIs under each Feature
                for (int p = 1; p <= 2; p++)
                {
                    var pbi = await CreateWorkItem("Product Backlog Item", $"Performance Test PBI {f}-{p}");
                    await CreateWorkItemLink(feature.Id, pbi.Id);
                    data.PBIs.Add(pbi);

                    // Create 3 Tasks under each PBI
                    for (int t = 1; t <= 3; t++)
                    {
                        var task = await CreateTaskWithCompletedWork($"Performance Test Task {f}-{p}-{t}", t * 2.0, GetRandomActivity());
                        await CreateWorkItemLink(pbi.Id, task.Id);
                        data.Tasks.Add(task);
                    }
                }
            }

            _logger.LogInformation("Small hierarchy created: {Epics} epics, {Features} features, {PBIs} PBIs, {Tasks} tasks", 
                data.Epics.Count, data.Features.Count, data.PBIs.Count, data.Tasks.Count);
            return data;
        }

        private async Task<TestHierarchyData> CreateMediumTestHierarchy()
        {
            _logger.LogInformation("Creating medium test hierarchy...");
            var data = new TestHierarchyData();

            // Create 2 Epics
            for (int e = 1; e <= 2; e++)
            {
                var epic = await CreateWorkItem("Epic", $"Performance Test Epic {e}");
                data.Epics.Add(epic);

                // Create 3 Features under each Epic
                for (int f = 1; f <= 3; f++)
                {
                    var feature = await CreateWorkItem("Feature", $"Performance Test Feature {e}-{f}");
                    await CreateWorkItemLink(epic.Id, feature.Id);
                    data.Features.Add(feature);

                    // Create 3-4 PBIs under each Feature
                    int pbiCount = f == 3 ? 4 : 3;
                    for (int p = 1; p <= pbiCount; p++)
                    {
                        var pbi = await CreateWorkItem("Product Backlog Item", $"Performance Test PBI {e}-{f}-{p}");
                        await CreateWorkItemLink(feature.Id, pbi.Id);
                        data.PBIs.Add(pbi);

                        // Create 3 Tasks under each PBI
                        for (int t = 1; t <= 3; t++)
                        {
                            var task = await CreateTaskWithCompletedWork($"Performance Test Task {e}-{f}-{p}-{t}", 
                                (t + p) * 1.5, GetRandomActivity());
                            await CreateWorkItemLink(pbi.Id, task.Id);
                            data.Tasks.Add(task);
                        }
                    }
                }
            }

            _logger.LogInformation("Medium hierarchy created: {Epics} epics, {Features} features, {PBIs} PBIs, {Tasks} tasks", 
                data.Epics.Count, data.Features.Count, data.PBIs.Count, data.Tasks.Count);
            return data;
        }

        private async Task<TestHierarchyData> CreateLargeTestHierarchy()
        {
            _logger.LogInformation("Creating large test hierarchy...");
            var data = new TestHierarchyData();

            // Create 5 Epics
            for (int e = 1; e <= 5; e++)
            {
                var epic = await CreateWorkItem("Epic", $"Performance Test Epic {e}");
                data.Epics.Add(epic);

                // Create 3 Features under each Epic
                for (int f = 1; f <= 3; f++)
                {
                    var feature = await CreateWorkItem("Feature", $"Performance Test Feature {e}-{f}");
                    await CreateWorkItemLink(epic.Id, feature.Id);
                    data.Features.Add(feature);

                    // Create 5 PBIs under each Feature
                    for (int p = 1; p <= 5; p++)
                    {
                        var pbi = await CreateWorkItem("Product Backlog Item", $"Performance Test PBI {e}-{f}-{p}");
                        await CreateWorkItemLink(feature.Id, pbi.Id);
                        data.PBIs.Add(pbi);

                        // Create 4 Tasks under each PBI
                        for (int t = 1; t <= 4; t++)
                        {
                            var task = await CreateTaskWithCompletedWork($"Performance Test Task {e}-{f}-{p}-{t}", 
                                (t + p + f) * 1.0, GetRandomActivity());
                            await CreateWorkItemLink(pbi.Id, task.Id);
                            data.Tasks.Add(task);
                        }
                    }
                }
            }

            _logger.LogInformation("Large hierarchy created: {Epics} epics, {Features} features, {PBIs} PBIs, {Tasks} tasks", 
                data.Epics.Count, data.Features.Count, data.PBIs.Count, data.Tasks.Count);
            return data;
        }

        private async Task CreateTestDataWithOldChangeDates()
        {
            _logger.LogInformation("Creating test data with old change dates...");
            
            // Create some work items but don't set recent change dates
            var epic = await CreateWorkItem("Epic", "Old Epic");
            var feature = await CreateWorkItem("Feature", "Old Feature");
            var pbi = await CreateWorkItem("Product Backlog Item", "Old PBI");
            var task = await CreateWorkItem("Task", "Old Task");

            // Set completed work but don't update change date (will be old)
            await UpdateWorkItemField(task.Id, "Microsoft.VSTS.Scheduling.CompletedWork", 8.0);
            await UpdateWorkItemField(task.Id, "Microsoft.VSTS.Common.Activity", "Development");

            await CreateWorkItemLink(epic.Id, feature.Id);
            await CreateWorkItemLink(feature.Id, pbi.Id);
            await CreateWorkItemLink(pbi.Id, task.Id);

            _logger.LogInformation("Old test data created");
        }

        private async Task<WorkItem> CreateWorkItem(string workItemType, string title)
        {
            var fields = new Dictionary<string, object?>
            {
                ["System.Title"] = title,
                ["System.WorkItemType"] = workItemType,
                ["System.TeamProject"] = _testProject
                // Don't set System.State - let Azure DevOps use the default state for the work item type
            };

            var workItem = await _realClient.CreateWorkItem(workItemType, fields);
            if (workItem == null || workItem.Id == 0)
            {
                throw new InvalidOperationException($"Failed to create work item: {title}");
            }

            _createdWorkItemIds.Add(workItem.Id);
            return workItem;
        }

        private async Task<WorkItem> CreateTaskWithCompletedWork(string title, double completedWork, string activity)
        {
            var task = await CreateWorkItem("Task", title);
            
            // Update with completed work and activity
            await UpdateWorkItemField(task.Id, "Microsoft.VSTS.Scheduling.CompletedWork", completedWork);
            await UpdateWorkItemField(task.Id, "Microsoft.VSTS.Common.Activity", activity);
            // Try common task completion states
            try
            {
                await UpdateWorkItemField(task.Id, "System.State", "Done");
            }
            catch
            {
                try
                {
                    await UpdateWorkItemField(task.Id, "System.State", "Closed");
                }
                catch
                {
                    try
                    {
                        await UpdateWorkItemField(task.Id, "System.State", "Resolved");
                    }
                    catch
                    {
                        // If none of the common completion states work, leave it in default state
                        _logger.LogWarning("Could not set task to completed state, leaving in default state");
                    }
                }
            }
            
            return task;
        }

        private async Task UpdateWorkItemField(int workItemId, string fieldName, object value)
        {
            var workItem = await _realClient.GetWorkItem(workItemId);
            if (workItem != null)
            {
                workItem.SetField(fieldName, value);
                await _realClient.SaveWorkItem(workItem);
            }
        }

        private async Task CreateWorkItemLink(int parentId, int childId)
        {
            var relations = new List<WorkItemRelation>
            {
                new WorkItemRelation
                {
                    RelationType = "Child",
                    RelatedWorkItemId = childId,
                    Rel = "System.LinkTypes.Hierarchy-Forward",
                    Url = $"https://dev.azure.com/{_testProject}/_apis/wit/workItems/{childId}"
                }
            };

            var parentWorkItem = await _realClient.GetWorkItem(parentId);
            if (parentWorkItem != null)
            {
                await _realClient.SaveWorkItemRelations(parentWorkItem, relations);
            }
        }

        private string GetRandomActivity()
        {
            var activities = new[] { "Development", "Testing", "Design", "Code Review", "Investigation" };
            return activities[Random.Shared.Next(activities.Length)];
        }

        private async Task<bool> ExecuteHierarchicalAggregationScript(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Executing hierarchical aggregation script...");
                
                // Create script runner
                using var scriptRunner = new ScheduledScriptTestRunner();
                
                // Execute the script
                var result = await scriptRunner.ExecuteScriptFromFileAsync(AGGREGATION_SCRIPT_PATH, cancellationToken);
                
                _logger.LogInformation("Script execution completed. Success: {Success}", result.Success);
                if (!result.Success && result.Exception != null)
                {
                    _logger.LogError(result.Exception, "Script execution failed: {ErrorMessage}", result.ErrorMessage);
                }

                // Log key performance metrics from the script
                foreach (var logMessage in result.LogMessages)
                {
                    if (logMessage.Contains("aggregation completed") || 
                        logMessage.Contains("Found") || 
                        logMessage.Contains("updated"))
                    {
                        _logger.LogInformation("Script Log: {Message}", logMessage);
                    }
                }

                return result.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute hierarchical aggregation script");
                return false;
            }
        }

        private async Task CleanupTestData()
        {
            if (_skipCleanup)
            {
                _logger.LogWarning("Skipping cleanup of {Count} test work items as requested", _createdWorkItemIds.Count);
                return;
            }

            _logger.LogInformation("Cleaning up {Count} test work items...", _createdWorkItemIds.Count);
            
            var cleanupTasks = _createdWorkItemIds.Select(async id =>
            {
                try
                {
                    var workItem = await _realClient.GetWorkItem(id);
                    if (workItem != null)
                    {
                        // Try different approaches to mark work item as deleted/inactive
                        try
                        {
                            // Try to delete by moving to 'Removed' state
                            workItem.SetField("System.State", "Removed");
                            await _realClient.SaveWorkItem(workItem);
                        }
                        catch
                        {
                            try
                            {
                                // Try 'Inactive' state
                                workItem.SetField("System.State", "Inactive");
                                await _realClient.SaveWorkItem(workItem);
                            }
                            catch
                            {
                                try
                                {
                                    // Try 'Closed' state
                                    workItem.SetField("System.State", "Closed");
                                    await _realClient.SaveWorkItem(workItem);
                                }
                                catch
                                {
                                    // If we can't change the state, just add a tag to mark it as test data
                                    var tags = workItem.GetField<string>("System.Tags") ?? "";
                                    if (!tags.Contains("PerformanceTest"))
                                    {
                                        workItem.SetField("System.Tags", string.IsNullOrEmpty(tags) ? "PerformanceTest" : $"{tags}; PerformanceTest");
                                        await _realClient.SaveWorkItem(workItem);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cleanup work item {WorkItemId}", id);
                }
            });

            await Task.WhenAll(cleanupTasks);
            _createdWorkItemIds.Clear();
            _logger.LogInformation("Cleanup completed");
        }

        public void Dispose()
        {
            // Note: Cleanup is handled explicitly in finally blocks for better control
            // If tests fail unexpectedly, work items might remain - consider manual cleanup
        }

        private class TestHierarchyData
        {
            public List<WorkItem> Epics { get; } = new();
            public List<WorkItem> Features { get; } = new();
            public List<WorkItem> PBIs { get; } = new();
            public List<WorkItem> Tasks { get; } = new();

            public int TotalWorkItems => Epics.Count + Features.Count + PBIs.Count + Tasks.Count;
        }
    }
}