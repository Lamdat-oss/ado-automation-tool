using Lamdat.ADOAutomationTool.Tests.Framework;
using FluentAssertions;

namespace Lamdat.ADOAutomationTool.Tests.ScheduledScripts
{
    /// <summary>
    /// Example tests demonstrating how to test scheduled scripts
    /// </summary>
    public class ScheduledScriptExampleTests : ScheduledScriptTestBase
    {
        [Fact]
        public async Task SimpleLoggingScript_ShouldExecuteSuccessfully()
        {
            // Arrange
            var script = @"
                Logger.Information(""Test script is running!"");
                var user = await Client.WhoAmI();
                Logger.Information($""Running as: {user?.Identity?.DisplayName}"");
            ";

            // Act
            var result = await ExecuteScriptAsync(script);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("Test script is running!");
            result.ShouldHaveLogMessageContaining("Running as: Test User");
        }

        [Fact]
        public async Task WorkItemCreationScript_ShouldCreateNewWorkItem()
        {
            // Arrange
            var script = @"
                Logger.Information(""Creating a new work item..."");
                
                var newWorkItem = new WorkItem
                {
                    Fields = new Dictionary<string, object?>
                    {
                        [""System.Title""] = ""Automated Test Item"",
                        [""System.WorkItemType""] = ""Task"",
                        [""System.State""] = ""New"",
                        [""System.TeamProject""] = Client.Project
                    }
                };
                
                var saved = await Client.SaveWorkItem(newWorkItem);
                Logger.Information($""Work item created with ID: {newWorkItem.Id}"");
            ";

            // Act
            var result = await ExecuteScriptAsync(script);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("Creating a new work item");
            result.ShouldHaveLogMessageContaining("Work item created with ID:");
            
            MockClient.ShouldHaveSavedWorkItems(1);
            
            var savedWorkItem = MockClient.SavedWorkItems.First();
            savedWorkItem.ShouldHaveTitle("Automated Test Item");
            savedWorkItem.ShouldHaveWorkItemType("Task");
            savedWorkItem.ShouldHaveState("New");
        }

        [Fact]
        public async Task WorkItemQueryScript_ShouldQueryExistingWorkItems()
        {
            // Arrange
            // Create some test work items
            CreateTestWorkItem("Bug", "Test Bug 1", "Active");
            CreateTestWorkItem("Task", "Test Task 1", "New");
            CreateTestWorkItem("Bug", "Test Bug 2", "Resolved");

            var script = @"
                Logger.Information(""Querying work items..."");
                
                var queryParams = new QueryLinksByWiqlPrms
                {
                    Wiql = ""SELECT [System.Id] FROM WorkItems WHERE [System.WorkItemType] = 'Bug'""
                };
                
                var bugs = await Client.QueryLinksByWiql(queryParams);
                Logger.Information($""Found {bugs.Count} bugs"");
                
                foreach (var bug in bugs)
                {
                    var title = bug.GetField<string>(""System.Title"");
                    var state = bug.GetField<string>(""System.State"");
                    Logger.Information($""Bug: {bug.Id} - {title} ({state})"");
                }
            ";

            // Act
            var result = await ExecuteScriptAsync(script);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("Querying work items");
            result.ShouldHaveLogMessageContaining("Found 2 bugs");
            result.ShouldHaveLogMessageContaining("Test Bug 1");
            result.ShouldHaveLogMessageContaining("Test Bug 2");
            
            MockClient.ShouldHaveExecutedQueries(1);
        }

        [Fact]
        public async Task WorkItemUpdateScript_ShouldModifyExistingWorkItem()
        {
            // Arrange
            var existingWorkItem = CreateTestWorkItem("Task", "Original Title", "New");
            
            var script = $@"
                Logger.Information(""Updating work item {existingWorkItem.Id}..."");
                
                var workItem = await Client.GetWorkItem({existingWorkItem.Id});
                if (workItem != null)
                {{
                    workItem.SetField(""System.Title"", ""Updated Title"");
                    workItem.SetField(""System.State"", ""Active"");
                    workItem.SetField(""Custom.ProcessedBy"", ""Scheduled Script"");
                    
                    await Client.SaveWorkItem(workItem);
                    Logger.Information($""Updated work item {{workItem.Id}}"");
                }}
                else
                {{
                    Logger.Warning(""Work item not found"");
                }}
            ";

            // Act
            var result = await ExecuteScriptAsync(script);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining($"Updating work item {existingWorkItem.Id}");
            result.ShouldHaveLogMessageContaining($"Updated work item {existingWorkItem.Id}");
            
            MockClient.ShouldHaveSavedWorkItems(1);
            
            var updatedWorkItem = MockClient.SavedWorkItems.First();
            updatedWorkItem.ShouldHaveTitle("Updated Title");
            updatedWorkItem.ShouldHaveState("Active");
            updatedWorkItem.ShouldHaveField("Custom.ProcessedBy", "Scheduled Script");
        }

        [Fact]
        public async Task ErrorHandlingScript_ShouldFailGracefully()
        {
            // Arrange
            var script = @"
                Logger.Information(""Starting script that will fail..."");
                throw new InvalidOperationException(""Intentional test failure"");
            ";

            // Act
            var result = await ExecuteScriptAsync(script);

            // Assert
            result.ShouldFailWith<InvalidOperationException>();
            result.ErrorMessage.Should().Contain("Intentional test failure");
            result.ShouldHaveLogMessageContaining("Starting script that will fail");
        }

        [Fact]
        public async Task CancellationTokenScript_ShouldRespectCancellation()
        {
            // Arrange
            var script = @"
                Logger.Information(""Starting long-running operation..."");
                
                for (int i = 0; i < 100; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Delay(10, cancellationToken);
                    Logger.Information($""Iteration {i}"");
                }
                
                Logger.Information(""Operation completed"");
            ";

            // Act
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            var result = await ExecuteScriptAsync(script, cts.Token);

            // Assert
            result.ShouldFailWith<OperationCanceledException>();
            result.ShouldHaveLogMessageContaining("Starting long-running operation");
        }

        [Fact]
        public async Task IterationQueryScript_ShouldQueryTeamIterations()
        {
            // Arrange
            var teamName = "TestTeam";
            AddTestIteration(teamName, "Sprint 1", DateTime.Now.AddDays(-14), DateTime.Now);
            AddTestIteration(teamName, "Sprint 2", DateTime.Now.AddDays(1), DateTime.Now.AddDays(15));

            var script = $@"
                Logger.Information(""Querying team iterations..."");
                
                var iterations = await Client.GetAllTeamIterations(""{teamName}"");
                Logger.Information($""Found {{iterations.Count}} iterations for team {teamName}"");
                
                foreach (var iteration in iterations)
                {{
                    var startDate = iteration.StartDate?.ToString(""yyyy-MM-dd"") ?? ""Unknown"";
                    var endDate = iteration.EndDate?.ToString(""yyyy-MM-dd"") ?? ""Unknown"";
                    Logger.Information($""Iteration: {{iteration.Name}} ({{startDate}} to {{endDate}})"");
                }}
            ";

            // Act
            var result = await ExecuteScriptAsync(script);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("Querying team iterations");
            result.ShouldHaveLogMessageContaining("Found 2 iterations for team TestTeam");
            result.ShouldHaveLogMessageContaining("Sprint 1");
            result.ShouldHaveLogMessageContaining("Sprint 2");
        }

        [Fact]
        public async Task ExecuteScriptFromFile_ShouldLoadAndExecuteFromDisk()
        {
            // Arrange
            var scriptContent = @"
                Logger.Information(""Script loaded from file!"");
                var user = await Client.WhoAmI();
                var displayName = user?.Identity?.DisplayName ?? ""Unknown"";
                Logger.Information($""User: {displayName}"");
            ";
            
            var tempFile = Path.GetTempFileName();
            await File.WriteAllTextAsync(tempFile, scriptContent);

            try
            {
                // Act
                var result = await ExecuteScriptFromFileAsync(tempFile);

                // Assert
                result.ShouldBeSuccessful();
                result.ShouldHaveLogMessageContaining("Script loaded from file!");
                result.ShouldHaveLogMessageContaining("User: Test User");
            }
            finally
            {
                // Cleanup
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }
    }
}