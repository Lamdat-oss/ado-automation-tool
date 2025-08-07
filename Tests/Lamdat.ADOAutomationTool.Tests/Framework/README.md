# IScript and IScheduledScript Testing Framework

This testing framework provides comprehensive tools for testing both types of scripts in the ADO Automation Tool:
- **IScheduledScript** - Scripts that run on a schedule via ScheduledScriptEngine
- **IScript** - Scripts that run in response to webhook events via CSharpScriptEngine

## Overview

The testing framework consists of several key components:

### For IScheduledScript Testing:
1. **ScheduledScriptTestRunner** - Test runner for scheduled scripts
2. **ScheduledScriptTestResult** - Results of scheduled script execution
3. **ScheduledScriptTestBase** - Base class for scheduled script tests

### For IScript Testing:
1. **ScriptTestRunner** - Test runner for webhook/context-based scripts
2. **ScriptTestResult** - Results of webhook script execution
3. **ScriptTestBase** - Base class for webhook script tests

### Shared Components:
1. **MockAzureDevOpsClient** - Mock implementation of `IAzureDevOpsClient`
2. **ScheduledScriptAssertions** - Fluent assertion helpers for both types
3. **TestLogSink** - Captures log messages for verification

## Quick Start

### IScheduledScript Testing (Scheduled Tasks)

```csharp
public class MyScheduledScriptTests : ScheduledScriptTestBase
{
    [Fact]
    public async Task MyScheduledScript_ShouldCreateWorkItem()
    {
        // Arrange
        var script = @"
            Logger.Information(""Creating work item..."");
            var workItem = new WorkItem
            {
                Fields = new Dictionary<string, object?>
                {
                    [""System.Title""] = ""Scheduled Item"",
                    [""System.WorkItemType""] = ""Task"",
                    [""System.State""] = ""New""
                }
            };
            await Client.SaveWorkItem(workItem);
        ";

        // Act
        var result = await ExecuteScriptAsync(script);

        // Assert
        result.ShouldBeSuccessful();
        result.ShouldHaveLogMessageContaining("Creating work item");
        MockClient.ShouldHaveSavedWorkItems(1);
    }
}
```

### IScript Testing (Webhook Events)

```csharp
public class MyWebhookScriptTests : ScriptTestBase
{
    [Fact]
    public async Task MyWebhookScript_ShouldUpdateWorkItem()
    {
        // Arrange
        var workItem = CreateTestWorkItem("Bug", "Test Bug", "New");
        var script = @"
            Logger.Information($""Processing work item: {Self.Id}"");
            if (EventType == ""workitem.updated"")
            {
                Self.SetField(""System.State"", ""Active"");
                Logger.Information(""Work item activated"");
            }
        ";

        // Act
        var result = await ExecuteScriptAsync(script, workItem, "workitem.updated");

        // Assert
        result.ShouldBeSuccessful();
        result.ShouldHaveLogMessageContaining($"Processing work item: {workItem.Id}");
        result.ShouldHaveLogMessageContaining("Work item activated");
        workItem.ShouldHaveState("Active");
    }
}
```

## Framework Components

### MockAzureDevOpsClient

The mock client simulates Azure DevOps operations for both script types:

```csharp
// Create test work items
var workItem = MockClient.CreateTestWorkItem("Bug", "Test Bug", "Active");

// Add test iterations
MockClient.AddIteration("MyTeam", "Sprint 1", DateTime.Now.AddDays(-7), DateTime.Now.AddDays(7));

// Verify operations
MockClient.ShouldHaveSavedWorkItems(2);
MockClient.ShouldHaveExecutedQueries(1);
MockClient.ShouldHaveSavedRelations(0);
```

### ScheduledScriptTestRunner (IScheduledScript)

Execute scheduled scripts:

```csharp
using var testRunner = new ScheduledScriptTestRunner();

// Execute script code
var result = await testRunner.ExecuteScriptAsync(scriptCode);

// Execute script from file
var result = await testRunner.ExecuteScriptFromFileAsync("path/to/script.rule");

// Execute with cancellation
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var result = await testRunner.ExecuteScriptAsync(scriptCode, cts.Token);
```

### ScriptTestRunner (IScript)

Execute webhook scripts with full context control:

```csharp
using var testRunner = new ScriptTestRunner();

// Execute with simplified parameters
var result = await testRunner.ExecuteScriptAsync(scriptCode, workItem, "workitem.updated");

// Execute with full context control
var context = testRunner.CreateTestContext(workItem, "workitem.created");
var result = await testRunner.ExecuteScriptAsync(scriptCode, context);

// Execute from file
var result = await testRunner.ExecuteScriptFromFileAsync("path/to/script.rule", workItem);

// Create specialized contexts
var stateChangeContext = testRunner.CreateStateChangeContext(workItem, "New", "Active");
var fieldChangeContext = testRunner.CreateFieldChangeContext(workItem, "System.Title", "Old", "New");
```

### Test Results and Assertions

Both result types support the same assertion patterns:

```csharp
// Basic success/failure
result.ShouldBeSuccessful();
result.ShouldFail();
result.ShouldFailWith<InvalidOperationException>();

// Log message assertions
result.ShouldHaveLogMessage("Exact message");
result.ShouldHaveLogMessageContaining("partial message");

// Performance assertions
result.ShouldExecuteWithin(TimeSpan.FromSeconds(5));

// Work item assertions
var workItem = MockClient.SavedWorkItems.First();
workItem.ShouldHaveTitle("Expected Title");
workItem.ShouldHaveState("Active");
workItem.ShouldHaveField("Custom.Field", "Expected Value");
```

## Test Patterns

### 1. Scheduled Script Tests (IScheduledScript)

Scheduled scripts have access to:
- `Client`: IAzureDevOpsClient
- `Logger`: ILogger  
- `cancellationToken`: CancellationToken
- `ScriptRunId`: string

```csharp
[Fact]
public async Task ScheduledScript_ShouldCreateReports()
{
    // Arrange
    CreateTestWorkItems(5, "Bug", "Test Bug");
    
    var script = @"
        var queryParams = new QueryLinksByWiqlPrms
        {
            Wiql = ""SELECT [System.Id] FROM WorkItems WHERE [System.WorkItemType] = 'Bug'""
        };
        
        var bugs = await Client.QuetyLinksByWiql(queryParams);
        Logger.Information($""Daily report: Found {bugs.Count} bugs"");
    ";
    
    var result = await ExecuteScriptAsync(script);
    
    result.ShouldBeSuccessful();
    result.ShouldHaveLogMessageContaining("Daily report: Found 5 bugs");
}
```

### 2. Webhook Script Tests (IScript)

Webhook scripts have access to:
- `Client`: IAzureDevOpsClient
- `EventType`: string
- `Logger`: ILogger
- `Project`: string
- `RelationChanges`: Relations
- `Self`: WorkItem (the current work item)
- `SelfChanges`: Dictionary<string, object>
- `WebHookResource`: WebHookResourceUpdate
- `cancellationToken`: CancellationToken
- `ScriptRunId`: string

```csharp
[Fact]
public async Task WebhookScript_ShouldRespondToStateChange()
{
    // Arrange
    var workItem = CreateTestWorkItem("Task", "Test Task", "Active");
    var context = CreateStateChangeContext(workItem, "New", "Active");
    
    var script = @"
        if (SelfChanges.ContainsKey(""System.State""))
        {
            Logger.Information(""State change detected"");
            Self.SetField(""Custom.StateChanged"", DateTime.UtcNow);
        }
    ";
    
    var result = await ExecuteScriptAsync(script, context);
    
    result.ShouldBeSuccessful();
    result.ShouldHaveLogMessageContaining("State change detected");
    workItem.Fields.Should().ContainKey("Custom.StateChanged");
}
```

### 3. Work Item Query Tests

Both script types can query work items using WIQL:

```csharp
[Fact]
public async Task Script_ShouldQueryWorkItems()
{
    // Arrange
    CreateTestWorkItems(3, "Bug", "Test Bug");
    
    var script = @"
        var queryParams = new QueryLinksByWiqlPrms
        {
            Wiql = ""SELECT [System.Id] FROM WorkItems WHERE [System.WorkItemType] = 'Bug'""
        };
        
        var bugs = await Client.QuetyLinksByWiql(queryParams);
        Logger.Information($""Found {bugs.Count} bugs"");
    ";
    
    var result = await ExecuteScriptAsync(script);
    
    result.ShouldBeSuccessful();
    result.ShouldHaveLogMessageContaining("Found 3 bugs");
    MockClient.ShouldHaveExecutedQueries(1);
}
```

### 4. Error Handling Tests

```csharp
[Fact]
public async Task Script_ShouldHandleErrors()
{
    var script = @"
        try
        {
            throw new InvalidOperationException(""Test error"");
        }
        catch (Exception ex)
        {
            Logger.Warning($""Handled error: {ex.Message}"");
        }
    ";
    
    var result = await ExecuteScriptAsync(script);
    
    result.ShouldBeSuccessful();
    result.ShouldHaveLogMessageContaining("Handled error: Test error");
}
```

### 5. Event-Specific Tests

Test how scripts respond to different event types:

```csharp
[Fact]
public async Task Script_ShouldProcessCreationEvent()
{
    var workItem = CreateTestWorkItem("Feature", "New Feature", "New");
    var context = CreateWorkItemCreatedContext(workItem);
    
    var script = @"
        if (EventType == ""workitem.created"")
        {
            Logger.Information(""New work item created"");
            Self.SetField(""Custom.CreatedByScript"", true);
        }
    ";
    
    var result = await ExecuteScriptAsync(script, context);
    
    result.ShouldBeSuccessful();
    result.ShouldHaveLogMessageContaining("New work item created");
    workItem.ShouldHaveField("Custom.CreatedByScript", true);
}
```

### 6. Context-Specific Tests

Test scripts with specific context scenarios:

```csharp
[Fact]
public async Task Script_ShouldProcessFieldChanges()
{
    var workItem = CreateTestWorkItem("Bug", "Test Bug", "New");
    var context = CreateFieldChangeContext(workItem, "System.Title", "Old Title", "New Title");
    
    var script = @"
        if (SelfChanges.ContainsKey(""System.Title""))
        {
            Logger.Information(""Title changed"");
            Self.SetField(""Custom.TitleChangeProcessed"", true);
        }
    ";
    
    var result = await ExecuteScriptAsync(script, context);
    
    result.ShouldBeSuccessful();
    result.ShouldHaveLogMessageContaining("Title changed");
    workItem.ShouldHaveField("Custom.TitleChangeProcessed", true);
}
```

## Best Practices

### 1. Use Appropriate Base Classes
- Inherit from `ScheduledScriptTestBase` for scheduled script tests
- Inherit from `ScriptTestBase` for webhook script tests

### 2. Test Real Script Files
Test actual script files from your directories:

```csharp
// Scheduled scripts
[Fact]
public async Task RealScheduledScript_ShouldWork()
{
    var result = await ExecuteScriptFromFileAsync("../../../../Src/Lamdat.ADOAutomationTool/scheduled-scripts/my-script.rule");
    result.ShouldBeSuccessful();
}

// Webhook scripts  
[Fact]
public async Task RealWebhookScript_ShouldWork()
{
    var workItem = CreateTestWorkItem();
    var result = await ExecuteScriptFromFileAsync("../../../../Src/Lamdat.ADOAutomationTool/scripts/my-script.rule", workItem);
    result.ShouldBeSuccessful();
}
```

### 3. Test Different Event Types
For webhook scripts, test various event scenarios:

```csharp
// Test creation events
var createContext = CreateWorkItemCreatedContext(workItem);

// Test state changes
var stateContext = CreateStateChangeContext(workItem, "New", "Active");

// Test field changes
var fieldContext = CreateFieldChangeContext(workItem, "System.AssignedTo", null, "user@company.com");
```

### 4. Verify Side Effects
Always verify that scripts performed expected operations:

```csharp
// Verify work item changes
workItem.ShouldHaveState("Expected State");
workItem.ShouldHaveField("Custom.Field", "Expected Value");

// Verify mock client operations
MockClient.ShouldHaveSavedWorkItems(expectedCount);
MockClient.ShouldHaveExecutedQueries(expectedCount);

// Verify logging
result.ShouldHaveLogMessageContaining("Expected message");
```

## Running Tests

Run tests using the standard .NET test runner:

```bash
# Run all tests
dotnet test

# Run specific test class
dotnet test --filter "ClassName=MyScheduledScriptTests"

# Run specific test type
dotnet test --filter "FullyQualifiedName~ScheduledScripts"
dotnet test --filter "FullyQualifiedName~Scripts"

# Run tests with verbose output
dotnet test --logger "console;verbosity=detailed"
```

## Key Differences Between Script Types

| Feature | IScheduledScript | IScript |
|---------|------------------|---------|
| **Trigger** | Timer/Schedule | Webhook Events |
| **Context** | Minimal (Client, Logger) | Rich (WorkItem, Events, Changes) |
| **Work Item** | Must query/create | Provided via `Self` |
| **Event Info** | None | `EventType`, `SelfChanges`, `WebHookResource` |
| **Base Class** | `ScheduledScriptTestBase` | `ScriptTestBase` |
| **Result Type** | `ScheduledScriptTestResult` | `ScriptTestResult` |
| **Use Cases** | Reports, Maintenance, Cleanup | Automation, Validation, Workflows |

This framework provides comprehensive testing capabilities for both script types, allowing you to thoroughly validate your automation logic in a controlled environment.