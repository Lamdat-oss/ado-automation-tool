using Lamdat.ADOAutomationTool.Tests.Framework;
using Lamdat.ADOAutomationTool.Tests.Scripts;
using Lamdat.ADOAutomationTool.Tests.ScheduledScripts;
using FluentAssertions;

namespace Lamdat.ADOAutomationTool.Tests.Integration
{
    /// <summary>
    /// Integration tests demonstrating how to use both IScript and IScheduledScript testing frameworks together
    /// </summary>
    public class IntegratedScriptTests : IDisposable
    {
        private readonly ScriptTestRunner _webhookRunner;
        private readonly ScheduledScriptTestRunner _scheduledRunner;

        public IntegratedScriptTests()
        {
            _webhookRunner = new ScriptTestRunner();
            _scheduledRunner = new ScheduledScriptTestRunner();
        }

        [Fact]
        public async Task WorkflowTest_WebhookShouldTriggerFollowUpScheduledTask()
        {
            // Simulate a workflow where a webhook script tags work items 
            // and a scheduled script processes tagged items

            // Part 1: Webhook script tags work items when they are created
            var newWorkItem = _webhookRunner.CreateTestWorkItem("Bug", "High Priority Bug", "New");
            var webhookContext = _webhookRunner.CreateWorkItemCreatedContext(newWorkItem);
            
            var webhookScript = @"
                if (EventType == ""workitem.created"" && Self.WorkItemType == ""Bug"")
                {
                    Logger.Information($""New bug created: {Self.Id}"");
                    Self.SetField(""Custom.NeedsReview"", true);
                    Self.SetField(""Custom.CreatedDate"", DateTime.UtcNow.ToString(""yyyy-MM-dd""));
                    Logger.Information(""Bug marked for review"");
                }
            ";

            var webhookResult = await _webhookRunner.ExecuteScriptAsync(webhookScript, webhookContext);

            // Verify webhook script execution
            webhookResult.ShouldBeSuccessful();
            webhookResult.ShouldHaveLogMessageContaining("New bug created");
            webhookResult.ShouldHaveLogMessageContaining("Bug marked for review");
            newWorkItem.ShouldHaveField("Custom.NeedsReview", true);

            // Part 2: Copy the work item to the scheduled script's mock client
            // In a real scenario, both would share the same data store
            var scheduledWorkItem = _scheduledRunner.CreateTestWorkItem("Bug", "High Priority Bug", "New");
            scheduledWorkItem.SetField("Custom.NeedsReview", true);
            scheduledWorkItem.SetField("Custom.CreatedDate", DateTime.UtcNow.ToString("yyyy-MM-dd"));

            // Part 3: Scheduled script processes items marked for review
            var scheduledScript = @"
                Logger.Information(""Starting scheduled review process..."");
                
                var queryParams = new QueryLinksByWiqlPrms
                {
                    Wiql = ""SELECT [System.Id] FROM WorkItems WHERE [System.WorkItemType] = 'Bug'""
                };
                
                var bugs = await Client.QueryLinksByWiql(queryParams);
                Logger.Information($""Found {bugs.Count} bugs to check"");
                
                int reviewedCount = 0;
                foreach (var bug in bugs)
                {
                    var needsReview = bug.GetField<bool>(""Custom.NeedsReview"");
                    if (needsReview)
                    {
                        Logger.Information($""Processing bug {bug.Id} for review"");
                        bug.SetField(""System.AssignedTo"", ""review-team@company.com"");
                        bug.SetField(""Custom.ReviewAssigned"", DateTime.UtcNow.ToString());
                        bug.SetField(""Custom.NeedsReview"", false);
                        await Client.SaveWorkItem(bug);
                        reviewedCount++;
                        Logger.Information($""Bug {bug.Id} assigned for review"");
                    }
                }
                
                Logger.Information($""Scheduled review process completed. Processed {reviewedCount} bugs."");
            ";

            var scheduledResult = await _scheduledRunner.ExecuteScriptAsync(scheduledScript);

            // Verify scheduled script execution
            scheduledResult.ShouldBeSuccessful();
            scheduledResult.ShouldHaveLogMessageContaining("Starting scheduled review process");
            scheduledResult.ShouldHaveLogMessageContaining("Found 1 bugs to check");
            scheduledResult.ShouldHaveLogMessageContaining("Processing bug");
            scheduledResult.ShouldHaveLogMessageContaining("Bug");
            scheduledResult.ShouldHaveLogMessageContaining("assigned for review");
            scheduledResult.ShouldHaveLogMessageContaining("Processed 1 bugs");

            // Verify final state
            _scheduledRunner.MockClient.ShouldHaveSavedWorkItems(1);
            var processedWorkItem = _scheduledRunner.MockClient.SavedWorkItems.First();
            processedWorkItem.ShouldHaveField("System.AssignedTo", "review-team@company.com");
            processedWorkItem.ShouldHaveField("Custom.NeedsReview", false);
            processedWorkItem.Fields.Should().ContainKey("Custom.ReviewAssigned");
        }

        [Fact]
        public async Task StateTransitionWorkflow_ShouldHandleCompleteLifecycle()
        {
            // Test a complete work item lifecycle with multiple webhook triggers

            // 1. Work item created
            var workItem = _webhookRunner.CreateTestWorkItem("User Story", "New Feature Request", "New");
            var createdContext = _webhookRunner.CreateWorkItemCreatedContext(workItem);
            
            var creationScript = @"
                if (EventType == ""workitem.created"")
                {
                    Logger.Information($""New {Self.WorkItemType} created: {Self.Id}"");
                    Self.SetField(""Custom.Lifecycle.Created"", DateTime.UtcNow.ToString());
                    Self.SetField(""Custom.Lifecycle.Stage"", ""Created"");
                    Logger.Information(""Creation metadata added"");
                }
            ";

            var creationResult = await _webhookRunner.ExecuteScriptAsync(creationScript, createdContext);
            creationResult.ShouldBeSuccessful();
            workItem.ShouldHaveField("Custom.Lifecycle.Stage", "Created");

            // 2. Work item activated
            workItem.SetField("System.State", "Active");
            var activatedContext = _webhookRunner.CreateStateChangeContext(workItem, "New", "Active");
            
            var activationScript = @"
                if (SelfChanges.ContainsKey(""System.State""))
                {
                    var newState = Self.GetField<string>(""System.State"");
                    Logger.Information($""State changed to: {newState}"");
                    
                    if (newState == ""Active"")
                    {
                        Self.SetField(""Custom.Lifecycle.Activated"", DateTime.UtcNow.ToString());
                        Self.SetField(""Custom.Lifecycle.Stage"", ""InProgress"");
                        Self.SetField(""Custom.EstimatedHours"", 8);
                        Logger.Information(""Work item activated and estimated"");
                    }
                }
            ";

            var activationResult = await _webhookRunner.ExecuteScriptAsync(activationScript, activatedContext);
            activationResult.ShouldBeSuccessful();
            workItem.ShouldHaveField("Custom.Lifecycle.Stage", "InProgress");
            workItem.ShouldHaveField("Custom.EstimatedHours", 8);

            // 3. Work item completed
            workItem.SetField("System.State", "Closed");
            var completedContext = _webhookRunner.CreateStateChangeContext(workItem, "Active", "Closed");
            
            var completionScript = @"
                if (SelfChanges.ContainsKey(""System.State""))
                {
                    var newState = Self.GetField<string>(""System.State"");
                    Logger.Information($""State changed to: {newState}"");
                    
                    if (newState == ""Closed"")
                    {
                        Self.SetField(""Custom.Lifecycle.Completed"", DateTime.UtcNow.ToString());
                        Self.SetField(""Custom.Lifecycle.Stage"", ""Completed"");
                        
                        var estimatedHours = Self.GetField<int>(""Custom.EstimatedHours"");
                        Self.SetField(""Custom.ActualHours"", estimatedHours); // Simulate actual hours
                        
                        Logger.Information(""Work item completed with tracking data"");
                    }
                }
            ";

            var completionResult = await _webhookRunner.ExecuteScriptAsync(completionScript, completedContext);
            completionResult.ShouldBeSuccessful();
            workItem.ShouldHaveField("Custom.Lifecycle.Stage", "Completed");
            workItem.ShouldHaveField("Custom.ActualHours", 8);

            // Verify complete lifecycle tracking
            workItem.Fields.Should().ContainKey("Custom.Lifecycle.Created");
            workItem.Fields.Should().ContainKey("Custom.Lifecycle.Activated");
            workItem.Fields.Should().ContainKey("Custom.Lifecycle.Completed");
        }

        [Fact]
        public async Task ReportingWorkflow_ScheduledScriptShouldAggregateWebhookData()
        {
            // Simulate multiple webhook events creating data that scheduled script aggregates

            // 1. Create multiple work items with webhook processing
            var workItems = new List<Lamdat.ADOAutomationTool.Entities.WorkItem>();
            for (int i = 1; i <= 5; i++)
            {
                var item = _webhookRunner.CreateTestWorkItem("Task", $"Task {i}", "New");
                var context = _webhookRunner.CreateWorkItemCreatedContext(item);
                
                var processingScript = @"
                    if (EventType == ""workitem.created"")
                    {
                        var priority = new Random().Next(1, 4); // Random priority 1-3
                        Self.SetField(""Microsoft.VSTS.Common.Priority"", priority);
                        Self.SetField(""Custom.ProcessedByWebhook"", true);
                        Self.SetField(""Custom.ProcessedDate"", DateTime.UtcNow.ToString(""yyyy-MM-dd""));
                        Logger.Information($""Task {Self.Id} processed with priority {priority}"");
                    }
                ";

                await _webhookRunner.ExecuteScriptAsync(processingScript, context);
                workItems.Add(item);
            }

            // 2. Copy data to scheduled script environment (simulating shared data store)
            foreach (var item in workItems)
            {
                var scheduledItem = _scheduledRunner.CreateTestWorkItem("Task", item.GetField<string>("System.Title"), "New");
                scheduledItem.SetField("Microsoft.VSTS.Common.Priority", item.GetField<int>("Microsoft.VSTS.Common.Priority"));
                scheduledItem.SetField("Custom.ProcessedByWebhook", true);
                scheduledItem.SetField("Custom.ProcessedDate", DateTime.UtcNow.ToString("yyyy-MM-dd"));
            }

            // 3. Scheduled script generates reports
            var reportingScript = @"
                Logger.Information(""Starting daily task report generation..."");
                
                var queryParams = new QueryLinksByWiqlPrms
                {
                    Wiql = ""SELECT [System.Id] FROM WorkItems WHERE [System.WorkItemType] = 'Task'""
                };
                
                var tasks = await Client.QueryLinksByWiql(queryParams);
                Logger.Information($""Found {tasks.Count} tasks for reporting"");
                
                var priorityCounts = new Dictionary<int, int> { {1, 0}, {2, 0}, {3, 0} };
                int processedByWebhook = 0;
                
                foreach (var task in tasks)
                {
                    var priority = task.GetField<int>(""Microsoft.VSTS.Common.Priority"");
                    if (priorityCounts.ContainsKey(priority))
                    {
                        priorityCounts[priority]++;
                    }
                    
                    var webhookProcessed = task.GetField<bool>(""Custom.ProcessedByWebhook"");
                    if (webhookProcessed)
                    {
                        processedByWebhook++;
                    }
                }
                
                Logger.Information($""=== Daily Task Report ==="");
                Logger.Information($""Total Tasks: {tasks.Count}"");
                Logger.Information($""High Priority (1): {priorityCounts[1]}"");
                Logger.Information($""Medium Priority (2): {priorityCounts[2]}"");
                Logger.Information($""Low Priority (3): {priorityCounts[3]}"");
                Logger.Information($""Processed by Webhook: {processedByWebhook}"");
                Logger.Information($""Report generated at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}"");
            ";

            var reportResult = await _scheduledRunner.ExecuteScriptAsync(reportingScript);

            // Verify reporting
            reportResult.ShouldBeSuccessful();
            reportResult.ShouldHaveLogMessageContaining("Starting daily task report generation");
            reportResult.ShouldHaveLogMessageContaining("Found 5 tasks for reporting");
            reportResult.ShouldHaveLogMessageContaining("=== Daily Task Report ===");
            reportResult.ShouldHaveLogMessageContaining("Total Tasks: 5");
            reportResult.ShouldHaveLogMessageContaining("Processed by Webhook: 5");
            reportResult.ShouldHaveLogMessageContaining("Report generated at:");
        }

        public void Dispose()
        {
            _webhookRunner?.Dispose();
            _scheduledRunner?.Dispose();
        }
    }
}