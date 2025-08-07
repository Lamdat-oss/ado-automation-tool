# IScheduledScript Testing Framework

This testing framework provides comprehensive tools for testing scheduled scripts in the ADO Automation Tool. It enables you to create, execute, and validate scheduled scripts in an isolated testing environment.

## Overview

The testing framework consists of several key components:

1. **MockAzureDevOpsClient** - A mock implementation of `IAzureDevOpsClient` for testing
2. **ScheduledScriptTestRunner** - The main test runner for executing scripts
3. **ScheduledScriptTestResult** - Contains the results of script execution
4. **ScheduledScriptAssertions** - Fluent assertion helpers for validation
5. **ScheduledScriptTestBase** - Base class to simplify test creation

## Quick Start

### Basic Test Example

```csharp
public class MyScheduledScriptTests : ScheduledScriptTestBase
{
    [Fact]
    public async Task MyScript_ShouldCreateWorkItem()
    {
        // Arrange
        var script = @"
            Logger.Information(""Creating work item..."");
            var workItem = new WorkItem
            {
                Fields = new Dictionary<string, object?>
                {
                    [""System.Title""] = ""Test Item"",
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

## Framework Components

### MockAzureDevOpsClient

The mock client simulates Azure DevOps operations:

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

### ScheduledScriptTestRunner

Execute scripts and capture results:

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

### Test Results and Assertions

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

### 1. Work Item Creation Tests

```csharp
[Fact]
public async Task Script_ShouldCreateWorkItem()
{
    var script = CreateWorkItemCreationScript("New Bug", "Bug", "New");
    var result = await ExecuteScriptAsync(script);
    
    result.ShouldBeSuccessful();
    MockClient.ShouldHaveSavedWorkItems(1);
    MockClient.SavedWorkItems.First().ShouldHaveTitle("New Bug");
}
```

### 2. Work Item Query Tests

```csharp
[Fact]
public async Task Script_ShouldQueryWorkItems()
{
    // Arrange
    CreateTestWorkItems(3, "Bug", "Test Bug");
    
    var script = CreateWorkItemQueryScript("Bug");
    var result = await ExecuteScriptAsync(script);
    
    result.ShouldBeSuccessful();
    result.ShouldHaveLogMessageContaining("Found 3 Bug items");
    MockClient.ShouldHaveExecutedQueries(1);
}
```

### 3. Work Item Update Tests

```csharp
[Fact]
public async Task Script_ShouldUpdateWorkItem()
{
    // Arrange
    var workItem = CreateTestWorkItem("Task", "Original Title", "New");
    
    var script = CreateWorkItemUpdateScript(workItem.Id, "Updated Title", "Active");
    var result = await ExecuteScriptAsync(script);
    
    result.ShouldBeSuccessful();
    MockClient.ShouldHaveSavedWorkItems(1);
    MockClient.SavedWorkItems.First().ShouldHaveTitle("Updated Title");
}
```

### 4. Bulk Processing Tests

```csharp
[Fact]
public async Task Script_ShouldProcessBulkItems()
{
    // Arrange
    CreateTestWorkItems(10, "Task", "Bulk Task");
    
    var script = @"
        var queryParams = new QueryLinksByWiqlPrms
        {
            Wiql = ""SELECT [System.Id] FROM WorkItems WHERE [System.WorkItemType] = 'Task'""
        };
        
        var tasks = await Client.QuetyLinksByWiql(queryParams);
        foreach (var task in tasks)
        {
            task.SetField(""System.State"", ""Active"");
            await Client.SaveWorkItem(task);
        }
    ";
    
    var result = await ExecuteScriptAsync(script);
    
    result.ShouldBeSuccessful();
    MockClient.ShouldHaveSavedWorkItems(10);
}
```

### 5. Error Handling Tests

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

### 6. Cancellation Tests

```csharp
[Fact]
public async Task Script_ShouldRespectCancellation()
{
    var script = @"
        for (int i = 0; i < 1000; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(1, cancellationToken);
        }
    ";
    
    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
    var result = await ExecuteScriptAsync(script, cts.Token);
    
    result.ShouldFailWith<OperationCanceledException>();
}
```

### 7. Iteration-Based Tests

```csharp
[Fact]
public async Task Script_ShouldProcessCurrentSprint()
{
    // Arrange
    AddCurrentSprint("MyTeam", "Current Sprint");
    
    var script = @"
        var iterations = await Client.GetAllTeamIterations(""MyTeam"");
        var current = iterations.FirstOrDefault(i => 
            i.StartDate <= DateTime.Now && i.EndDate >= DateTime.Now);
        
        Logger.Information($""Current iteration: {current?.Name}"");
    ";
    
    var result = await ExecuteScriptAsync(script);
    
    result.ShouldBeSuccessful();
    result.ShouldHaveLogMessageContaining("Current iteration: Current Sprint");
}
```

## Best Practices

### 1. Use Base Classes
Inherit from `ScheduledScriptTestBase` for common functionality:

```csharp
public class MyScriptTests : ScheduledScriptTestBase
{
    // Tests here have access to TestRunner, MockClient, and helper methods
}
```

### 2. Setup and Cleanup
Use proper setup and cleanup:

```csharp
[Fact]
public async Task MyTest()
{
    // Arrange
    ClearTestData(); // Start with clean state
    var workItems = CreateTestWorkItems(5);
    
    // Act & Assert
    // ... test logic
}
```

### 3. Test Real Script Files
Test actual script files from your scheduled-scripts directory:

```csharp
[Fact]
public async Task RealScript_ShouldWork()
{
    var result = await ExecuteScriptFromFileAsync("../../../../Src/Lamdat.ADOAutomationTool/scheduled-scripts/my-script.rule");
    result.ShouldBeSuccessful();
}
```

### 4. Verify Side Effects
Always verify that your scripts performed the expected operations:

```csharp
// Verify work item operations
MockClient.ShouldHaveSavedWorkItems(expectedCount);
MockClient.ShouldHaveExecutedQueries(expectedCount);
MockClient.ShouldHaveSavedRelations(expectedCount);

// Verify log output
result.ShouldHaveLogMessageContaining("Expected message");

// Verify work item state
var workItem = MockClient.SavedWorkItems.First();
workItem.ShouldHaveState("Expected State");
```

### 5. Performance Testing
Test script performance:

```csharp
[Fact]
public async Task Script_ShouldCompleteQuickly()
{
    var result = await ExecuteScriptAsync(script);
    
    result.ShouldBeSuccessful();
    result.ShouldExecuteWithin(TimeSpan.FromSeconds(5));
}
```

## Running Tests

Run tests using the standard .NET test runner:

```bash
# Run all tests
dotnet test

# Run specific test class
dotnet test --filter "ClassName=MyScheduledScriptTests"

# Run tests with verbose output
dotnet test --logger "console;verbosity=detailed"
```

## Debugging Tips

1. **Check Log Messages**: Use `result.LogMessages` to see what your script logged
2. **Examine Mock State**: Inspect `MockClient.SavedWorkItems` and other collections
3. **Use Breakpoints**: Set breakpoints in your test methods to examine state
4. **Test Incrementally**: Start with simple scripts and build complexity

## Extending the Framework

You can extend the framework by:

1. Adding new assertion methods to `ScheduledScriptAssertions`
2. Creating custom mock clients for specific scenarios
3. Adding helper methods to `ScheduledScriptTestBase`
4. Creating specialized test result types for complex scenarios

This framework provides a solid foundation for testing all aspects of your scheduled scripts in a controlled, repeatable environment.