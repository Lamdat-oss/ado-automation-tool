# IScript and IScheduledScript Testing Framework

This testing framework provides comprehensive tools for testing both types of scripts in the ADO Automation Tool:
- **IScheduledScript** - Legacy scheduled scripts that run on a global schedule
- **IScheduledScriptWithInterval** - New interval-aware scheduled scripts that define their own execution intervals
- **IScript** - Scripts that run in response to webhook events via CSharpScriptEngine

## Overview

The testing framework consists of several key components:

### For IScheduledScript Testing (with Interval Support):
1. **ScheduledScriptTestRunner** - Test runner for scheduled scripts (supports both legacy and interval-aware)
2. **ScheduledScriptTestResult** - Results of scheduled script execution with interval information
3. **ScheduledScriptTestBase** - Base class for scheduled script tests

### For IScript Testing:
1. **ScriptTestRunner** - Test runner for webhook/context-based scripts
2. **ScriptTestResult** - Results of webhook script execution
3. **ScriptTestBase** - Base class for webhook script tests

### Shared Components:
1. **MockAzureDevOpsClient** - Mock implementation of `IAzureDevOpsClient`
2. **ScheduledScriptAssertions** - Fluent assertion helpers for both types (enhanced with interval support)
3. **TestLogSink** - Captures log messages for verification

## Quick Start

### IScheduledScript Testing (Interval-Aware Scheduled Tasks)

```csharp
public class MyScheduledScriptTests : ScheduledScriptTestBase
{
    [Fact]
    public async Task MyScheduledScript_ShouldCreateWorkItemWithInterval()
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
            
            // Return result with 30-minute interval
            return ScheduledScriptResult.Success(30, ""Task created successfully"");
        ";

        // Act
        var result = await ExecuteScriptAsync(script);

        // Assert
        result.ShouldBeSuccessful();
        result.ShouldBeIntervalAware();
        result.ShouldHaveNextInterval(30);
        result.ShouldReturnSuccessfulResult();
        result.ShouldHaveResultMessage(""Task created successfully"");
        result.ShouldHaveLogMessageContaining(""Creating work item"");
        MockClient.ShouldHaveSavedWorkItems(1);
    }
    
    [Fact]
    public async Task MyLegacyScheduledScript_ShouldWork()
    {
        // Arrange - Legacy script without interval return
        var script = @"
            Logger.Information(""Legacy task running"");
            // No return statement - uses global interval
        ";

        // Act
        var result = await ExecuteScriptAsync(script);

        // Assert
        result.ShouldBeSuccessful();
        result.ShouldUseLegacyMode(); // Not interval-aware
        result.ShouldHaveLogMessageContaining(""Legacy task running"");
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

### Enhanced ScheduledScriptTestRunner

The test runner now automatically detects and supports both script types:

```csharp
using var testRunner = new ScheduledScriptTestRunner();

// Execute interval-aware script
var intervalScript = @"
    Logger.Information(""Task running"");
    return ScheduledScriptResult.Success(15, ""Next run in 15 minutes"");
";
var result = await testRunner.ExecuteScriptAsync(intervalScript);
result.ShouldBeIntervalAware();
result.ShouldHaveNextInterval(15);

// Execute legacy script (automatically detected)
var legacyScript = @"
    Logger.Information(""Legacy task running"");
    // No return statement
";
var legacyResult = await testRunner.ExecuteScriptAsync(legacyScript);
legacyResult.ShouldUseLegacyMode();

// Execute script from file
var result = await testRunner.ExecuteScriptFromFileAsync("path/to/script.rule");

// Execute with cancellation
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var result = await testRunner.ExecuteScriptAsync(scriptCode, cts.Token);
```

### Enhanced Test Results and Assertions

#### Interval-Aware Script Assertions

```csharp
// Interval-specific assertions
result.ShouldBeIntervalAware();
result.ShouldUseLegacyMode();
result.ShouldHaveNextInterval(30);
result.ShouldReturnSuccessfulResult();
result.ShouldReturnFailedResult();
result.ShouldHaveResultMessage("Expected message");
result.ShouldHaveResultMessageContaining("partial message");

// Basic success/failure (works for both types)
result.ShouldBeSuccessful();
result.ShouldFail();
result.ShouldFailWith<InvalidOperationException>();

// Log message assertions (works for both types)
result.ShouldHaveLogMessage("Exact message");
result.ShouldHaveLogMessageContaining("partial message");

// Performance assertions (works for both types)
result.ShouldExecuteWithin(TimeSpan.FromSeconds(5));
```

#### Accessing Interval Information

```csharp
// Check if script is interval-aware
if (result.IsIntervalAware)
{
    var nextInterval = result.NextExecutionIntervalMinutes;
    var scriptResult = result.ScheduledScriptResult;
    var isSuccess = scriptResult.IsSuccess;
    var message = scriptResult.Message;
}
```

## Test Patterns

### 1. Interval-Aware Scheduled Script Tests

Test scripts that return `ScheduledScriptResult` with custom intervals:

```csharp
[Theory]
[InlineData(5)]     // Every 5 minutes
[InlineData(60)]    // Every hour
[InlineData(1440)]  // Daily
[InlineData(10080)] // Weekly
public async Task ScheduledScript_ShouldReturnSpecifiedInterval(int minutes)
{
    var script = $@"
        Logger.Information(""Task running with {minutes} minute interval"");
        return ScheduledScriptResult.Success({minutes}, ""Task completed"");
    ";
    
    var result = await ExecuteScriptAsync(script);
    
    result.ShouldBeSuccessful();
    result.ShouldBeIntervalAware();
    result.ShouldHaveNextInterval(minutes);
    result.ShouldReturnSuccessfulResult();
}
```

### 2. Dynamic Interval Scripts

Test scripts that adjust intervals based on conditions:

```csharp
[Fact]
public async Task SmartIntervalScript_ShouldAdjustBasedOnConditions()
{
    var script = @"
        var currentHour = DateTime.Now.Hour;
        int interval = currentHour >= 9 && currentHour <= 17 ? 5 : 30;
        string reason = currentHour >= 9 && currentHour <= 17 
            ? ""Business hours - frequent checks"" 
            : ""After hours - less frequent checks"";
        
        Logger.Information($""Setting interval to {interval} minutes: {reason}"");
        return ScheduledScriptResult.Success(interval, reason);
    ";
    
    var result = await ExecuteScriptAsync(script);
    
    result.ShouldBeSuccessful();
    result.ShouldBeIntervalAware();
    
    var currentHour = DateTime.Now.Hour;
    var expectedInterval = currentHour >= 9 && currentHour <= 17 ? 5 : 30;
    result.ShouldHaveNextInterval(expectedInterval);
}
```

### 3. Error Handling with Retry Intervals

Test scripts that return different intervals on success vs. failure:

```csharp
[Fact]
public async Task HealthCheckScript_ShouldRetryFasterOnFailure()
{
    var failingScript = @"
        try
        {
            throw new Exception(""Health check failed"");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, ""Health check failed"");
            // Retry in 2 minutes on failure instead of normal 10 minutes
            return ScheduledScriptResult.Success(2, $""Health check failed, retrying soon: {ex.Message}"");
        }
    ";
    
    var result = await ExecuteScriptAsync(failingScript);
    
    result.ShouldBeSuccessful();
    result.ShouldBeIntervalAware();
    result.ShouldHaveNextInterval(2); // Shorter retry interval
    result.ShouldReturnSuccessfulResult();
    result.ShouldHaveResultMessageContaining("retrying soon");
}
```

### 4. Failure Result Testing

Test scripts that return failure results:

```csharp
[Fact]
public async Task ScheduledScript_ShouldReturnFailureResult()
{
    var script = @"
        try
        {
            throw new InvalidOperationException(""Something went wrong"");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, ""Task failed"");
            return ScheduledScriptResult.Failure($""Task failed: {ex.Message}"");
        }
    ";
    
    var result = await ExecuteScriptAsync(script);
    
    result.ShouldBeSuccessful(); // Script executed without throwing
    result.ShouldBeIntervalAware();
    result.ShouldReturnFailedResult();
    result.ShouldHaveResultMessage("Task failed: Something went wrong");
}
```

### 5. Legacy Script Compatibility

Test that legacy scripts continue to work:

```csharp
[Fact]
public async Task LegacyScript_ShouldWorkWithoutChanges()
{
    var legacyScript = @"
        Logger.Information(""Legacy task running"");
        var user = await Client.WhoAmI();
        Logger.Information(""Legacy task completed"");
        // No return statement - uses legacy interface
    ";
    
    var result = await ExecuteScriptAsync(legacyScript);
    
    result.ShouldBeSuccessful();
    result.ShouldUseLegacyMode();
    result.ShouldHaveLogMessageContaining("Legacy task completed");
}
```

### 6. Real Script File Testing

Test actual script files from your directories:

```csharp
[Theory]
[InlineData("01-sample-task.rule", true, 2)]        // Should be interval-aware with 2-minute interval
[InlineData("02-daily-report.rule", true, 1440)]    // Should be interval-aware with daily interval
[InlineData("03-health-check.rule", true, 10)]      // Should be interval-aware with 10-minute interval
[InlineData("05-legacy-task.rule", false, null)]    // Should be legacy mode
public async Task RealScheduledScript_ShouldWorkCorrectly(string filename, bool shouldBeIntervalAware, int? expectedInterval)
{
    var scriptPath = $"../../../../Src/Lamdat.ADOAutomationTool/scheduled-scripts/{filename}";
    
    if (File.Exists(scriptPath))
    {
        var result = await ExecuteScriptFromFileAsync(scriptPath);
        
        result.ShouldBeSuccessful();
        
        if (shouldBeIntervalAware)
        {
            result.ShouldBeIntervalAware();
            if (expectedInterval.HasValue)
            {
                result.ShouldHaveNextInterval(expectedInterval.Value);
            }
        }
        else
        {
            result.ShouldUseLegacyMode();
        }
    }
}
```

## Script Types Comparison

| Feature | IScheduledScript (Legacy) | IScheduledScriptWithInterval (New) | IScript (Webhook) |
|---------|---------------------------|-------------------------------------|-------------------|
| **Trigger** | Timer/Global Schedule | Timer/Individual Schedule | Webhook Events |
| **Interval Control** | Global configuration | Per-script return value | N/A |
| **Context** | Minimal (Client, Logger) | Minimal (Client, Logger) | Rich (WorkItem, Events) |
| **Return Value** | None (void) | ScheduledScriptResult | None (void) |
| **Test Method** | `ShouldUseLegacyMode()` | `ShouldBeIntervalAware()` | N/A |
| **Use Cases** | Simple scheduled tasks | Smart scheduled tasks | Event-driven automation |

## Migration Guide for Tests

### From Legacy to Interval-Aware Tests

Old test (legacy):
```csharp
[Fact]
public async Task ScheduledScript_ShouldWork()
{
    var script = @"
        Logger.Information(""Task running"");
    ";
    
    var result = await ExecuteScriptAsync(script);
    result.ShouldBeSuccessful();
}
```

New test (interval-aware):
```csharp
[Fact]
public async Task ScheduledScript_ShouldWorkWithInterval()
{
    var script = @"
        Logger.Information(""Task running"");
        return ScheduledScriptResult.Success(30, ""Task completed"");
    ";
    
    var result = await ExecuteScriptAsync(script);
    result.ShouldBeSuccessful();
    result.ShouldBeIntervalAware();
    result.ShouldHaveNextInterval(30);
    result.ShouldReturnSuccessfulResult();
}
```

## Best Practices

### 1. Test Both Script Types
Always test both legacy and interval-aware scripts in your test suites.

### 2. Test Different Intervals
Use theory tests to verify various interval values work correctly.

### 3. Test Conditional Intervals
Verify that scripts adjust intervals based on conditions (time of day, success/failure, etc.).

### 4. Test Actual Script Files
Include tests that execute your real script files to ensure they work correctly.

### 5. Verify Side Effects
Always verify that scripts performed expected operations and returned correct interval information.

This enhanced framework provides comprehensive testing capabilities for the new interval-based scheduled script functionality while maintaining full backward compatibility with existing legacy scripts.