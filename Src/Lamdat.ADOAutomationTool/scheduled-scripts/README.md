# Scheduled Tasks

The ADO Automation Tool now supports executing C# scripts on a timer schedule. This feature allows you to run maintenance tasks, generate reports, send notifications, or perform any other automated operations at regular intervals.

## Configuration

### Settings

Add the following setting to your `appsettings.json` file:

```json
{
  "Settings": {
    "ScheduledTaskIntervalMinutes": 5
  }
}
```

- `ScheduledTaskIntervalMinutes`: The interval in minutes between scheduled task executions (default: 5 minutes)

## Creating Scheduled Scripts

1. Create a `scheduled-scripts` directory in the application root (this will be created automatically if it doesn't exist)
2. Add C# script files with the `.rule` extension
3. Scripts are executed in alphabetical order by filename

### Script Structure

Scheduled scripts have access to the same context as webhook scripts:

```csharp
// Your C# code here
Logger.Information($"Scheduled task started at {DateTime.Now}");

try 
{
    // Access Azure DevOps client
    var user = await Client.WhoAmI();
    Logger.Information($"Running as: {user?.Identity?.DisplayName}");
    
    // Query work items, create reports, etc.
    // Your automation logic here
    
    Logger.Information("Scheduled task completed successfully");
}
catch (Exception ex)
{
    Logger.Error(ex, "Error in scheduled task");
    throw;
}
```

### Available Context

In scheduled scripts, you have access to:

- `Client`: IAzureDevOpsClient - Azure DevOps client for API operations
- `Logger`: ILogger - Serilog logger for output
- `EventType`: String - Always "ScheduledTask" for scheduled executions
- `Project`: String - Can be set by scripts if needed
- `Self`: WorkItem - A placeholder work item (ID = 0)
- `cancellationToken`: CancellationToken - For handling timeouts

### Example Use Cases

1. **Daily Reports**: Generate and email daily work item reports
2. **Cleanup Tasks**: Archive completed work items or update stale items
3. **Notifications**: Send reminders for overdue tasks
4. **Data Synchronization**: Sync data between Azure DevOps and external systems
5. **Health Checks**: Monitor project health and alert on issues

## Logging

Scheduled task execution is logged with detailed information:
- Start/completion times
- Script execution duration
- Any errors or warnings
- Success/failure status

## Service Management

The scheduled task service:
- Starts automatically when the application starts
- Runs continuously on the configured interval
- Prevents overlapping executions
- Handles errors gracefully without stopping the service
- Uses the same script engine as webhook handlers for consistency

## Performance Considerations

- Scripts should be designed to complete quickly
- Heavy operations should be designed with cancellation token support
- Consider the interval frequency vs. script execution time
- Use appropriate logging levels to avoid log spam