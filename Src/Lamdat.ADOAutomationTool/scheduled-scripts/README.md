# Scheduled Tasks

The ADO Automation Tool now supports executing C# scripts on a timer schedule with **individual script intervals**. Each script can define its own execution frequency, allowing for flexible scheduling from frequent health checks to daily reports.

## Features

- **Per-Script Intervals**: Each script can define its own execution interval
- **Backward Compatibility**: Existing scripts continue to work with global interval
- **Intelligent Scheduling**: Only scripts that are due for execution are run
- **Memory Tracking**: Last run times are tracked in memory for each script
- **Flexible Return Values**: Scripts can return success, failure, and next interval information

## Configuration

### Settings

Add the following setting to your `appsettings.json` file:

```json
{
  "Settings": {
    "ScheduledTaskIntervalMinutes": 1
  }
}
```

- `ScheduledTaskIntervalMinutes`: How often the service checks for scripts to execute (recommended: 1 minute for fine-grained control)

## Creating Scheduled Scripts

1. Create a `scheduled-scripts` directory in the application root (this will be created automatically if it doesn't exist)
2. Add C# script files with the `.rule` extension
3. Scripts are executed in alphabetical order by filename, but only when their individual intervals have elapsed

### Script Structure Options

#### Option 1: Interval-Aware Scripts (Recommended)

Scripts that return a `ScheduledScriptResult` with custom intervals:

```csharp
// Your C# code here
Logger.Information($"Task started at {DateTime.Now}");

try 
{
    // Access Azure DevOps client
    var user = await Client.WhoAmI();
    Logger.Information($"Running as: {user?.Identity?.DisplayName}");
    
    // Your automation logic here
    Logger.Information("Task completed successfully");
    
    // Return success with custom interval (e.g., 30 minutes)
    return ScheduledScriptResult.Success(30, "Task scheduled for next 30 minutes");
}
catch (Exception ex)
{
    Logger.Error(ex, "Error in scheduled task");
    
    // Return failure or retry with different interval
    return ScheduledScriptResult.Success(5, $"Task failed, will retry in 5 minutes: {ex.Message}");
}
```

#### Option 2: Legacy Scripts (Backward Compatible)

Existing scripts that don't return intervals work unchanged:

```csharp
// Your existing C# code here
Logger.Information($"Legacy task started at {DateTime.Now}");

var user = await Client.WhoAmI();
Logger.Information($"Running as: {user?.Identity?.DisplayName}");

// Your automation logic here
Logger.Information("Legacy task completed successfully");

// No return statement - uses global default interval
```

### Available Context

In scheduled scripts, you have access to:

- `Client`: IAzureDevOpsClient - Azure DevOps client for API operations
- `Logger`: ILogger - Serilog logger for output
- `EventType`: String - Always "ScheduledTask" for scheduled executions
- `Project`: String - Can be set by scripts if needed
- `Self`: WorkItem - A placeholder work item (ID = 0)
- `cancellationToken`: CancellationToken - For handling timeouts

### ScheduledScriptResult Options

```csharp
// Success with custom interval (in minutes)
return ScheduledScriptResult.Success(60, "Run again in 1 hour");

// Success with default interval
return ScheduledScriptResult.Success();

// Success with different interval based on conditions
var nextInterval = DateTime.Now.Hour < 9 ? 60 : 10; // Every hour before 9 AM, every 10 minutes after
return ScheduledScriptResult.Success(nextInterval, $"Next run in {nextInterval} minutes");

// Failure (will use default interval for retry)
return ScheduledScriptResult.Failure("Task failed, will retry with default interval");
```

### Example Intervals

- **Every 2 minutes**: `return ScheduledScriptResult.Success(2);`
- **Every 15 minutes**: `return ScheduledScriptResult.Success(15);`
- **Every hour**: `return ScheduledScriptResult.Success(60);`
- **Every 4 hours**: `return ScheduledScriptResult.Success(240);`
- **Daily**: `return ScheduledScriptResult.Success(1440);`
- **Weekly**: `return ScheduledScriptResult.Success(10080);`

### Example Use Cases

1. **Health Checks**: Run every 5-10 minutes to monitor system health
2. **Daily Reports**: Run once per day to generate summary reports
3. **Hourly Synchronization**: Sync data every hour during business hours
4. **Weekly Cleanup**: Archive old data or perform maintenance weekly
5. **Critical Alerts**: Check for urgent conditions every 1-2 minutes
6. **Resource Monitoring**: Monitor quotas and usage every 30 minutes

## Logging

The service provides detailed logging:
- When each script is due for execution
- Execution start/completion times
- Script execution duration
- Next scheduled execution time for each script
- Any errors or warnings

Example log output:
```
[INFO] Script 'health-check.rule' scheduled for execution (interval: 10 min, last run: 2024-01-15 10:00:00)
[INFO] Script 'health-check.rule' completed. Next execution in 10 minutes at 2024-01-15 10:10:00
```

## Service Management

The scheduled task service:
- Checks for scripts to execute every minute (configurable)
- Only executes scripts when their individual intervals have elapsed
- Tracks last execution time for each script in memory
- Prevents overlapping executions
- Handles errors gracefully without stopping the service
- Maintains backward compatibility with existing scripts

## Performance Considerations

- The service checks for due scripts frequently (every 1 minute by default)
- Only scripts that are actually due for execution are run
- Memory usage is minimal - only tracks last run times and intervals
- Scripts should complete quickly relative to their intervals
- Use appropriate intervals to avoid overwhelming the system
- Consider business hours and system load when setting intervals

## Migration Guide

### From Global Intervals to Per-Script Intervals

1. **Existing scripts** continue to work without changes
2. **To add custom intervals**: Add a return statement with `ScheduledScriptResult.Success(intervalMinutes)`
3. **Recommended**: Update `ScheduledTaskIntervalMinutes` to 1 minute in configuration
4. **Test**: Verify scripts run at expected intervals using the logs

### Example Migration

Before (legacy):
```csharp
Logger.Information("Task running");
// Task logic here
Logger.Information("Task completed");
```

After (with custom interval):
```csharp
Logger.Information("Task running");
// Task logic here
Logger.Information("Task completed");

// Add custom interval
return ScheduledScriptResult.Success(30, "Task will run again in 30 minutes");