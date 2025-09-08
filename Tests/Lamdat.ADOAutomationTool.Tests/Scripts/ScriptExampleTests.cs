using Lamdat.ADOAutomationTool.Tests.Framework;
using Lamdat.ADOAutomationTool.Entities;
using FluentAssertions;

namespace Lamdat.ADOAutomationTool.Tests.Scripts
{
    /// <summary>
    /// Example tests demonstrating how to test webhook/context-based scripts (IScript)
    /// </summary>
    public class ScriptExampleTests : ScriptTestBase
    {
        [Fact]
        public async Task SimpleContextScript_ShouldExecuteSuccessfully()
        {
            // Arrange
            var workItem = CreateTestWorkItem("Bug", "Test Bug", "New");
            var script = @"
                Logger.Information($""Processing work item: {Self.Id}"");
                Logger.Information($""Event type: {EventType}"");
                var title = Self.GetField<string>(""System.Title"");
                Logger.Information($""Work item title: {title}"");
            ";

            // Act
            var result = await ExecuteScriptAsync(script, workItem, "workitem.updated");

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining($"Processing work item: {workItem.Id}");
            result.ShouldHaveLogMessageContaining("Event type: workitem.updated");
            result.ShouldHaveLogMessageContaining("Work item title: Test Bug");
        }

        [Fact]
        public async Task WorkItemFieldUpdateScript_ShouldModifyWorkItem()
        {
            // Arrange
            var workItem = CreateTestWorkItem("Task", "Original Task", "New");
            var script = @"
                Logger.Information($""Updating work item {Self.Id}"");
                Self.SetField(""System.Title"", ""Updated Task"");
                Self.SetField(""System.State"", ""Active"");
                Self.SetField(""Custom.UpdatedBy"", ""Test Script"");
                Logger.Information(""Work item updated successfully"");
            ";

            // Act
            var result = await ExecuteScriptAsync(script, workItem);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining($"Updating work item {workItem.Id}");
            result.ShouldHaveLogMessageContaining("Work item updated successfully");
            
            // Verify the work item was modified
            workItem.ShouldHaveTitle("Updated Task");
            workItem.ShouldHaveState("Active");
            workItem.ShouldHaveField("Custom.UpdatedBy", "Test Script");
        }

        [Fact]
        public async Task StateChangeEventScript_ShouldRespondToStateChange()
        {
            // Arrange
            var workItem = CreateTestWorkItem("Bug", "Test Bug", "Active");
            var context = CreateStateChangeContext(workItem, "New", "Active");
            var script = @"
                Logger.Information($""Event: {EventType}"");
                if (SelfChanges.ContainsKey(""System.State""))
                {
                    Logger.Information(""State change detected"");
                    Logger.Information($""Work item {Self.Id} state changed"");
                    Self.SetField(""Custom.StateChangeProcessed"", DateTime.UtcNow.ToString());
                }
            ";

            // Act
            var result = await ExecuteScriptAsync(script, context);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("State change detected");
            result.ShouldHaveLogMessageContaining($"Work item {workItem.Id} state changed");
            workItem.Fields.Should().ContainKey("Custom.StateChangeProcessed");
        }

        [Fact]
        public async Task WorkItemCreationEventScript_ShouldProcessNewWorkItem()
        {
            // Arrange
            var workItem = CreateTestWorkItem("User Story", "New Feature", "New");
            var context = CreateWorkItemCreatedContext(workItem);
            var script = @"
                Logger.Information($""New work item created: {Self.Id}"");
                Logger.Information($""Event: {EventType}"");
                
                if (EventType == ""workitem.created"")
                {
                    Logger.Information(""Processing new work item..."");
                    Self.SetField(""Custom.CreatedByScript"", true);
                    Self.SetField(""Custom.ProcessedDate"", DateTime.UtcNow.ToString(""yyyy-MM-dd""));
                    Logger.Information(""New work item processing completed"");
                }
            ";

            // Act
            var result = await ExecuteScriptAsync(script, context);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining($"New work item created: {workItem.Id}");
            result.ShouldHaveLogMessageContaining("Event: workitem.created");
            result.ShouldHaveLogMessageContaining("Processing new work item");
            result.ShouldHaveLogMessageContaining("New work item processing completed");
            
            workItem.ShouldHaveField("Custom.CreatedByScript", true);
            workItem.Fields.Should().ContainKey("Custom.ProcessedDate");
        }

        [Fact]
        public async Task QueryRelatedWorkItemsScript_ShouldFindLinkedItems()
        {
            // Arrange
            var parentItem = CreateTestWorkItem("Epic", "Parent Epic", "New");
            var childItem1 = CreateTestWorkItem("User Story", "Child Story 1", "New");
            var childItem2 = CreateTestWorkItem("User Story", "Child Story 2", "New");
            
            // Set up relationships
            parentItem.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = childItem1.Id });
            parentItem.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = childItem2.Id });
            
            var script = @"
                Logger.Information($""Processing epic: {Self.Id}"");
                
                var queryParams = new QueryLinksByWiqlPrms
                {
                    SourceWorkItemId = Self.Id,
                    SourceWorkItemType = ""Epic"",
                    LinkType = ""Child"",
                    TargetWorkItemType = ""User Story""
                };
                
                var childStories = await Client.QueryLinksByWiql(queryParams);
                Logger.Information($""Found {childStories.Count} child stories"");
                
                foreach (var story in childStories)
                {
                    var title = story.GetField<string>(""System.Title"");
                    Logger.Information($""Child story: {story.Id} - {title}"");
                }
            ";

            // Act
            var result = await ExecuteScriptAsync(script, parentItem);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining($"Processing epic: {parentItem.Id}");
            result.ShouldHaveLogMessageContaining("Found 2 child stories");
            result.ShouldHaveLogMessageContaining("Child Story 1");
            result.ShouldHaveLogMessageContaining("Child Story 2");
            
            MockClient.ShouldHaveExecutedQueries(1);
        }

        [Fact]
        public async Task ConditionalProcessingScript_ShouldProcessBasedOnWorkItemType()
        {
            // Arrange
            var bugItem = CreateTestWorkItem("Bug", "Critical Bug", "New");
            var script = @"
                Logger.Information($""Processing work item: {Self.WorkItemType}"");
                
                if (Self.WorkItemType == ""Bug"")
                {
                    Logger.Information(""Processing bug item"");
                    Self.SetField(""Microsoft.VSTS.Common.Priority"", 1);
                    Self.SetField(""System.AssignedTo"", ""bug-team@company.com"");
                    Logger.Information(""Bug escalated to high priority"");
                }
                else if (Self.WorkItemType == ""Task"")
                {
                    Logger.Information(""Processing task item"");
                    Self.SetField(""Custom.TaskCategory"", ""Standard"");
                }
                else
                {
                    Logger.Information($""No special processing for {Self.WorkItemType}"");
                }
            ";

            // Act
            var result = await ExecuteScriptAsync(script, bugItem);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("Processing work item: Bug");
            result.ShouldHaveLogMessageContaining("Processing bug item");
            result.ShouldHaveLogMessageContaining("Bug escalated to high priority");
            
            bugItem.ShouldHaveField("Microsoft.VSTS.Common.Priority", 1);
            bugItem.ShouldHaveField("System.AssignedTo", "bug-team@company.com");
        }

        [Fact]
        public async Task WebHookResourceProcessingScript_ShouldAccessWebHookData()
        {
            // Arrange
            var workItem = CreateTestWorkItem("Task", "Test Task", "New");
            var script = @"
                Logger.Information($""Processing webhook resource for work item: {WebHookResource.WorkItemId}"");
                Logger.Information($""Webhook resource ID: {WebHookResource.Id}"");
                Logger.Information($""Revision: {WebHookResource.Revision}"");
                
                if (WebHookResource.Fields.ContainsKey(""System.Title""))
                {
                    var title = WebHookResource.Fields[""System.Title""];
                    Logger.Information($""Title from webhook: {title}"");
                }
                
                Logger.Information(""Webhook resource processing completed"");
            ";

            // Act
            var result = await ExecuteScriptAsync(script, workItem);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining($"Processing webhook resource for work item: {workItem.Id}");
            result.ShouldHaveLogMessageContaining($"Webhook resource ID: {workItem.Id}");
            result.ShouldHaveLogMessageContaining("Title from webhook: Test Task");
            result.ShouldHaveLogMessageContaining("Webhook resource processing completed");
        }

        [Fact]
        public async Task ErrorHandlingScript_ShouldHandleExceptionsGracefully()
        {
            // Arrange
            var workItem = CreateTestWorkItem("Task", "Test Task", "New");
            var script = @"
                Logger.Information(""Starting script with error handling"");
                
                try
                {
                    Logger.Information(""Performing risky operation..."");
                    throw new InvalidOperationException(""Simulated error"");
                }
                catch (Exception ex)
                {
                    Logger.Warning($""Caught exception: {ex.Message}"");
                    Self.SetField(""Custom.ErrorHandled"", true);
                    Self.SetField(""Custom.ErrorMessage"", ex.Message);
                }
                
                Logger.Information(""Script completed with error handling"");
            ";

            // Act
            var result = await ExecuteScriptAsync(script, workItem);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("Starting script with error handling");
            result.ShouldHaveLogMessageContaining("Performing risky operation");
            result.ShouldHaveLogMessageContaining("Caught exception: Simulated error");
            result.ShouldHaveLogMessageContaining("Script completed with error handling");
            
            workItem.ShouldHaveField("Custom.ErrorHandled", true);
            workItem.ShouldHaveField("Custom.ErrorMessage", "Simulated error");
        }

        [Fact]
        public async Task ProjectSpecificScript_ShouldAccessProjectInformation()
        {
            // Arrange
            var workItem = CreateTestWorkItem("Feature", "Project Feature", "New");
            var projectName = "TestProject";
            var script = @"
                Logger.Information($""Processing work item in project: {Project}"");
                Logger.Information($""Client project: {Client.Project}"");
                
                if (Project == ""TestProject"")
                {
                    Logger.Information(""Processing for test project"");
                    Self.SetField(""Custom.ProjectType"", ""Test"");
                }
                
                Logger.Information(""Project-specific processing completed"");
            ";

            // Act
            var result = await ExecuteScriptAsync(script, workItem, "workitem.updated", projectName);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining($"Processing work item in project: {projectName}");
            result.ShouldHaveLogMessageContaining($"Client project: {projectName}");
            result.ShouldHaveLogMessageContaining("Processing for test project");
            result.ShouldHaveLogMessageContaining("Project-specific processing completed");
            
            workItem.ShouldHaveField("Custom.ProjectType", "Test");
        }

        [Fact]
        public async Task ScriptFromFile_ShouldExecuteFromDisk()
        {
            // Arrange
            var workItem = CreateTestWorkItem("Bug", "File Bug", "New");
            var scriptContent = @"
                Logger.Information(""Script loaded from file"");
                var title = Self.GetField<string>(""System.Title"");
                Logger.Information($""Processing work item {Self.Id}: {title}"");
                Self.SetField(""Custom.ProcessedFromFile"", true);
                Logger.Information(""File script processing completed"");
            ";
            
            var tempFile = Path.GetTempFileName();
            await File.WriteAllTextAsync(tempFile, scriptContent);

            try
            {
                // Act
                var result = await ExecuteScriptFromFileAsync(tempFile, workItem);

                // Assert
                result.ShouldBeSuccessful();
                result.ShouldHaveLogMessageContaining("Script loaded from file");
                result.ShouldHaveLogMessageContaining($"Processing work item {workItem.Id}: File Bug");
                result.ShouldHaveLogMessageContaining("File script processing completed");
                
                workItem.ShouldHaveField("Custom.ProcessedFromFile", true);
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