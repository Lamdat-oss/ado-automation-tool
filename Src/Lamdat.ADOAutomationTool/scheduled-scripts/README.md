# Scheduled Tasks

The ADO Automation Tool now supports executing C# scripts on a timer schedule with **individual script intervals** and **last run tracking**. Each script can define its own execution frequency and access information about when it was last executed, enabling powerful incremental data processing scenarios.

## Features

- **Per-Script Intervals**: Each script can define its own execution interval
- **Last Run Tracking**: Scripts have access to their last execution timestamp for incremental processing
- **Configurable Default Last Run**: Set default last run date for first executions after system restart
- **Backward Compatibility**: Existing scripts continue to work with global interval
- **Intelligent Scheduling**: Only scripts that are due for execution are run
- **Memory Tracking**: Last run times are tracked in memory for each script
- **Flexible Return Values**: Scripts can return success, failure, and next interval information

## Configuration

### Settings

Add the following settings to your `appsettings.json` file:

```json
{
  "Settings": {
    "ScheduledTaskIntervalMinutes": 1,
    "ScheduledScriptDefaultLastRun": "7"
  }
}
```

- `ScheduledTaskIntervalMinutes`: How often the service checks for scripts to execute (recommended: 1 minute for fine-grained control)
- `ScheduledScriptDefaultLastRun`: Default last run date for scripts on first execution after system restart
  - Can be an ISO date string: `"2024-01-01T00:00:00Z"`
  - Can be number of days ago: `"7"` (7 days ago)
  - If not set, defaults to current time

## Creating Scheduled Scripts

1. Create a `scheduled-scripts` directory in the application root (this will be created automatically if it doesn't exist)
2. Add C# script files with the `.rule` extension
3. Scripts are executed in alphabetical order by filename, but only when their individual intervals have elapsed

### Script Structure Options

#### Option 1: Interval-Aware Scripts with LastRun (Recommended)

Scripts that return a `ScheduledScriptResult` with custom intervals and can access last run information:

```csharp
// Your C# code here
Logger.Information($"Task started at {DateTime.Now}");
Logger.Information($"Last run was: {LastRun:yyyy-MM-dd HH:mm:ss}");

try 
{
    // Access Azure DevOps client
    var user = await Client.WhoAmI();
    Logger.Information($"Running as: {user?.Identity?.DisplayName}");
    
    // Process work items changed since last run
    var sinceLastRun = LastRun.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
    var queryParams = new QueryLinksByWiqlPrms
    {
        Wiql = $@"SELECT [System.Id] FROM WorkItems 
                  WHERE [System.ChangedDate] >= '{sinceLastRun}' 
                  ORDER BY [System.ChangedDate]"
    };
    
    var changedItems = await Client.QueryLinksByWiql(queryParams);
    Logger.Information($"Found {changedItems.Count} items changed since last run");
    
    // Process the changed items
    foreach (var item in changedItems)
    {
        // Your processing logic here
        Logger.Debug($"Processing item {item.Id}");
    }
    
    Logger.Information("Task completed successfully");
    
    // Return success with custom interval (e.g., 30 minutes)
    return ScheduledScriptResult.Success(30, $"Processed {changedItems.Count} items, next run in 30 minutes");
}
catch (Exception ex)
{
    Logger.Error(ex, "Error in scheduled task");
    
    // Return failure or retry with different interval
    return ScheduledScriptResult.Success(5, $"Task failed, will retry in 5 minutes: {ex.Message}");
}
```

#### Option 2: Legacy Scripts (Backward Compatible)

Existing scripts that don't return intervals work unchanged and now have access to LastRun:

```csharp
// Your existing C# code here
Logger.Information($"Legacy task started at {DateTime.Now}");
Logger.Information($"Last run was: {LastRun:yyyy-MM-dd HH:mm:ss}");

var user = await Client.WhoAmI();
Logger.Information($"Running as: {user?.Identity?.DisplayName}");

// Your automation logic here - can now optionally use LastRun
var timeSinceLastRun = DateTime.Now - LastRun;
if (timeSinceLastRun.TotalHours > 1)
{
    Logger.Information("More than an hour since last run - performing full sync");
    // Full processing logic
}
else
{
    Logger.Information("Recent run - performing incremental update");
    // Incremental processing logic
}

Logger.Information("Legacy task completed successfully");

// No return statement - uses global default interval
```

### Available Context

In scheduled scripts, you have access to:

- `Client`: IAzureDevOpsClient - Azure DevOps client for API operations
- `Logger`: ILogger - Serilog logger for output
- `LastRun`: DateTime - When this script was last executed
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
var changeCount = GetChangesSinceLastRun();
var nextInterval = changeCount > 50 ? 5 : 30; // More frequent if high activity
return ScheduledScriptResult.Success(nextInterval, $"Processed {changeCount} changes, next run in {nextInterval} minutes");

// Failure (will use default interval for retry)
return ScheduledScriptResult.Failure("Task failed, will retry with default interval");
```

### LastRun Usage Patterns

#### 1. Incremental Data Processing
```csharp
// Query for items changed since last run
var sinceLastRun = LastRun.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
var query = $"SELECT [System.Id] FROM WorkItems WHERE [System.ChangedDate] >= '{sinceLastRun}'";
```

#### 2. Time-Based Logic
```csharp
var timeSinceLastRun = DateTime.Now - LastRun;
if (timeSinceLastRun.TotalHours > 24)
{
    // Daily processing
}
else if (timeSinceLastRun.TotalMinutes > 60)
{
    // Hourly processing
}
```

#### 3. Activity-Based Intervals
```csharp
var changes = GetChangesSinceLastRun(LastRun);
var nextInterval = changes.Count > 100 ? 5 : 30; // Adjust based on activity
return ScheduledScriptResult.Success(nextInterval, $"Activity level: {changes.Count} changes");
```

### Example Intervals

- **Every 2 minutes**: `return ScheduledScriptResult.Success(2);`
- **Every 15 minutes**: `return ScheduledScriptResult.Success(15);`
- **Every hour**: `return ScheduledScriptResult.Success(60);`
- **Every 4 hours**: `return ScheduledScriptResult.Success(240);`
- **Daily**: `return ScheduledScriptResult.Success(1440);`
- **Weekly**: `return ScheduledScriptResult.Success(10080);`

### Example Use Cases

1. **Incremental Sync**: Process only work items changed since last run
2. **Delta Reports**: Generate reports covering the period since last execution
3. **Activity Monitoring**: Adjust frequency based on system activity levels
4. **Time-Window Processing**: Handle different logic for business hours vs. off-hours
5. **Catch-Up Processing**: Detect long gaps since last run and handle accordingly
6. **Progressive Processing**: Break large datasets into chunks across multiple runs

## Logging

The service provides detailed logging including last run information:
- When each script is due for execution
- Last run timestamp for each script
- Execution start/completion times
- Script execution duration
- Next scheduled execution time for each script
- Any errors or warnings

Example log output:
```
[INFO] First execution for script 'data-sync.rule' (Last run will be: 2024-01-15 03:00:00)
[INFO] Script 'data-sync.rule' completed. Last run: 2024-01-15 03:00:00, Next execution in 30 minutes at 2024-01-15 10:30:00
```

## Service Management

The scheduled task service:
- Checks for scripts to execute every minute (configurable)
- Only executes scripts when their individual intervals have elapsed
- Tracks last execution time for each script in memory
- Provides default last run date for first executions after system restart
- Prevents overlapping executions
- Handles errors gracefully without stopping the service
- Maintains backward compatibility with existing scripts

## Configuration Examples

### Days Ago Configuration
```json
{
  "Settings": {
    "ScheduledScriptDefaultLastRun": "7"
  }
}
```
Sets default last run to 7 days ago for first-time executions.

### Specific Date Configuration
```json
{
  "Settings": {
    "ScheduledScriptDefaultLastRun": "2024-01-01T00:00:00Z"
  }
}
```
Sets default last run to a specific date/time.

### Current Time (Default)
```json
{
  "Settings": {
    "ScheduledScriptDefaultLastRun": null
  }
}
```
Uses current time as default last run (same as not setting the value).

## Performance Considerations

- The service checks for due scripts frequently (every 1 minute by default)
- Only scripts that are actually due for execution are run
- Memory usage is minimal - only tracks last run times and intervals
- LastRun tracking enables efficient incremental processing
- Use appropriate intervals to avoid overwhelming the system
- Consider business hours and system load when setting intervals
- Implement batching for large datasets in incremental processing

## Migration Guide

### From Global Intervals to Per-Script Intervals with LastRun

1. **Existing scripts** continue to work without changes and now have access to LastRun
2. **To add custom intervals**: Add a return statement with `ScheduledScriptResult.Success(intervalMinutes)`
3. **To use LastRun**: Access the `LastRun` variable for incremental processing
4. **Recommended**: Update `ScheduledTaskIntervalMinutes` to 1 minute in configuration
5. **Configure**: Set `ScheduledScriptDefaultLastRun` for appropriate default behavior
6. **Test**: Verify scripts run at expected intervals and process incremental data correctly

### Example Migration

Before (legacy):
```csharp
Logger.Information("Processing all work items");
var allItems = await Client.QueryAllWorkItems();
ProcessItems(allItems);
```

After (with LastRun and intervals):
```csharp
Logger.Information($"Processing work items changed since {LastRun:yyyy-MM-dd HH:mm:ss}");
var changedItems = await Client.QueryWorkItemsSince(LastRun);
ProcessItems(changedItems);

// Return custom interval based on activity
var nextInterval = changedItems.Count > 50 ? 10 : 30;
return ScheduledScriptResult.Success(nextInterval, $"Processed {changedItems.Count} items");