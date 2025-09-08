# ADO Automation Tool

The **ADO Automation Tool** is a web API designed to listen to webhooks from Azure DevOps. When a webhook is received, it triggers a script defined in the `scripts` folder with the `.rule` file suffix. Additionally, the tool supports executing C# scripts on a timer schedule for automated tasks and data processing.

## Configuration

The tool can be configured using either a JSON configuration file, command line arguments, or environment variables.
Scripts/rules folder can be mounted into /app/scripts folder

### TLS configuration
1.  create pfx for tls using PowerShell
```powershell
$cert = New-SelfSignedCertificate -KeyLength 2048 -KeyAlgorithm RSA -Type SSLServerAuthentication -FriendlyName "adoAutomationTool" -NotAfter 2030-01-01 -Subject "adoautomationtool.example.com")
$certPass = Read-Host -Prompt "Password" -AsSecureString
Export-PfxCertificate -FilePath "adoautomation.pfx" -Cert $cert -Password $certPass
```
2. set the path of the pfx file to use in environment variable 'Kestrel__Endpoints__Https__Certificate__Path', command line or the app settings file.
3. set the password for the pfx in environment variable 'Kestrel__Endpoints__Https__Certificate__Password' command line or the app settings file.
4. mount the pfx to /app folder

### JSON Configuration

To use JSON configuration, create a `config.json` file with the following structure:

```json
{
  "Settings": {
    "CollectionURL": "",
    "PAT": "",
    "ScriptExecutionTimeoutSeconds": 60,
    "ScheduledScriptExecutionTimeoutSeconds": 3600,
    "MaxQueueWebHookRequestCount": 1000,
    "BypassRules": true,
    "SharedKey": "",
    "AllowedCorsOrigin": "*",
    "NotValidCertificates": false,
    "EnableAutoHttpsRedirect": true,
    "ScheduledTaskIntervalMinutes": 1,
    "ScheduledScriptDefaultLastRun": "7"
  }
}
```

### Command Line Arguments

You can also specify configuration settings through command line arguments when running the application. Here's how you can do it:

```bash
docker run --rm -it  -v ./Examples:/app/scripts   -p 5000:5000/tcp   -e "SETTINGS__COLLECTIONURL=https:///<azure-devops-host>/<collection> | dev.azure.com>/<org>" -e  "SETTINGS__PAT=<PAT>" -e "SETTINGS__BYPASSRULES=true" -e "SETTINGS__SHAREDKEY=<key>" adoautomationtool/adoautomationtool:latest

# with https
docker run -p 5000:5000/tcp  -p 5001:5001 --rm -it  -v ./Examples:/app/scripts -v ./adoautomation.pfx:/app/adoautomation.pfx -e -e ASPNETCORE_HTTPS_PORT=5001 -e Kestrel__Endpoints__Https__Certificate__Password="***" -e Kestrel__Endpoints__Https__Certificate__Path=/app/adoautomation.pfx  -e "SETTINGS__COLLECTIONURL=https://azuredevops.syncnow.io/NovaCollection" -e  "SETTINGS__PAT=****" -e "SETTINGS__BYPASSRULES=true" -e "SETTINGS__SHAREDKEY=***"   adoautomationtool/adoautomationtool:0.1.74
```

### Environment Variables

Alternatively, you can use environment variables to configure the tool. Set the following environment variables:

- `SETTINGS__COLLECTIONURL`: URL of the Azure DevOps collection.
- `SETTINGS__PAT`: Personal Access Token (PAT) used for authentication.
- `SETTINGS__BYPASSRULES`: Boolean value indicating whether to bypass Azure DevOps rules.
- `SETTINGS__SHAREDKEY`: Key used to authenticate to the web service.
- `SETTINGS__NOTVALIDCERTIFICATES`: If to allow working with not valid azure devops certificates
- `SETTINGS__ENABLEAUTOHTTPSREDIRECT`: If to enable auto http to https redirect
- `SETTINGS__SCRIPTEXECUTIONTIMEOUTSECONDS`: Script execution timeout in seconds for webhook scripts, default is 60 seconds.
- `SETTINGS__SCHEDULEDSCRIPTEXECUTIONTIMEOUTSECONDS`: Script execution timeout in seconds for scheduled scripts, default is 3600 seconds (1 hour). If not specified, falls back to `ScriptExecutionTimeoutSeconds`.
- `SETTINGS__MAXQUEUEWEBHOOKREQUESTCOUNT`: Maximum number of webhook requests to queue before rejecting new ones, default is 1000.
- `SETTINGS__SCHEDULEDTASKINTERVALMINUTES`: How often the service checks for scheduled scripts to execute (recommended: 1 minute for fine-grained control)
- `SETTINGS__SCHEDULEDSCRIPTDEFAULTLASTRUN`: Default last run date for scripts on first execution after system restart

## Usage

1. Clone this repository to your local machine.
2. Configure the settings using one of the methods described above.
3. Deploy the application to your desired hosting environment.
4. Start the application.
5. Configure azure devops service hooks to point to your ado automation tool url
   In Azure DevOps Project Configuration -> Service hooks. 
   - Create new service hooks 'work item created', 'workitem updated' with links and without the checkbox 'Links are added or removed' which is used for links changes.
   - For every defined service hook - set the url of your adoautomationtool server with webhook as the url - https://example.com/WebHook
   - Set the username - can be any user name or empty
   - Set your shared key defined in the configuration file or with environment variable (SETTINGS__SHAREDKEY)

5. Your ADO Automation Tool is now ready to receive webhooks and execute scripts.

## Script Execution Timeouts

The ADO Automation Tool supports separate timeout configurations for different types of scripts:

### Webhook Scripts (Default: 60 seconds)
- **Configuration**: `ScriptExecutionTimeoutSeconds`
- **Purpose**: Scripts triggered by Azure DevOps webhooks
- **Recommended**: Keep relatively short (60-300 seconds) since webhooks should respond quickly
- **Example**: Processing work item updates, creating related items, sending notifications

### Scheduled Scripts (Default: 3600 seconds / 1 hour)
- **Configuration**: `ScheduledScriptExecutionTimeoutSeconds`
- **Purpose**: Scripts executed on a timer schedule
- **Recommended**: Can be longer (300-3600+ seconds) for complex data processing tasks
- **Example**: Bulk data processing, report generation, cleanup tasks, synchronization

### Configuration Examples

```json
{
  "Settings": {
    "ScriptExecutionTimeoutSeconds": 120,
    "ScheduledScriptExecutionTimeoutSeconds": 1800
  }
}
```

```bash
# Environment variables
-e "SETTINGS__SCRIPTEXECUTIONTIMEOUTSECONDS=120"
-e "SETTINGS__SCHEDULEDSCRIPTEXECUTIONTIMEOUTSECONDS=1800"
```

### Timeout Behavior
- If a script exceeds its timeout, it will be cancelled and an error will be logged
- Webhook timeouts are typically shorter to ensure quick webhook responses
- Scheduled script timeouts can be longer to accommodate complex processing
- If `ScheduledScriptExecutionTimeoutSeconds` is not specified, it falls back to `ScriptExecutionTimeoutSeconds`

# Webhook Scripts (Rules Language)

## Example Rule
```csharp
Logger.Information($"Received event type: {EventType}, Work Item Id: {Self.Id}, Title: '{Self.Title}, State: {Self.State}, WorkItemType:  {Self.WorkItemType}");

Self.Fields["System.Description"] = $"Current Date: {DateTime.Today}";
if(Self.Parent != null){
    var parentWit = await Client.GetWorkItem(Self.Parent.RelatedWorkItemId);
    Logger.Information($"Work Item Parent Title: {parentWit.Title}");
    if(parentWit != null)
    {
        Logger.Information($"Work Item Parent Children Count: {parentWit.Children.Count}");
    }
}
```

## Usage of C# Context in .rule Files
In your `.rule` files, you'll have access to a C# context to interact with Azure DevOps objects. Here's how you can use it:

- `Self`: Represents the current work item being processed.
- `SelfChanges`: Represents the current work item changes, dictionary object.
- `RelationChanges`: Represents the current work item relations changes - reprsented by this foratt
    ```
          "RelationChanges": {
                "Removed": [
                    {
                        "Attributes": {
                            "IsLocked": false,
                            "Name": "Child"
                        },
                        "Rel": "System.LinkTypes.Hierarchy-Forward",
                        "Url": "https://azuredevops.example.com/NovaCollection/cf5ef574-eece-4cf6-947f-0d1dbb1a1a60/_apis/wit/workItems/5"
                    }
                ],
                 "Added": [
                    {
                        "Attributes": {
                            "IsLocked": false,
                            "Name": "Child"
                        },
                        "Rel": "System.LinkTypes.Hierarchy-Reverse",
                        "Url": "https://azuredevops.example.com/NovaCollection/cf5ef574-eece-4cf6-947f-0d1dbb1a1a60/_apis/wit/workItems/2"
                    }
            },
    ```
- `Logger`: Allows logging messages.
- `Client`: Provides access to Azure DevOps API for fetching additional work item details.

Example usage:
```csharp
// Checking if IterationPath has changed
if (selfChanges.Fields.ContainsKey("System.IterationPath"))
{
    var iterChange = selfChanges.Fields["System.IterationPath"];
    return $"Iteration has changed from '{iterChange.OldValue}' to '{iterChange.NewValue}'";
}

// Accessing work item properties
Logger.Information($"Work Item Title: {Self.Title}");

// Accessing parent work item
if(Self.Parent != null){
    var parentWit = await Client.GetWorkItem(Self.Parent.RelatedWorkItemId);
    Logger.Information($"Parent Work Item Title: {parentWit.Title}");
}
```

## Logger Methods

The ADO Automation Tool uses Serilog for logging. Here are the available logger methods you can use:

### `Logger.Information`

Logs informational messages. Useful for general information about the application's flow.

```csharp
Logger.Information("This is an informational message");
Logger.Information("Received event type: {EventType}, Work Item Id: {WorkItemId}", eventType, workItemId);
```

### `Logger.Debug`

Logs debug messages. Useful for detailed debugging information.

```csharp
Logger.Debug("This is a debug message");
Logger.Debug("Debugging event type: {EventType}", eventType);
```

### `Logger.Error`

Logs error messages. Useful for logging errors and exceptions.

```csharp
Logger.Error("This is an error message");
Logger.Error(ex, "An error occurred while processing event type: {EventType}", eventType);
```

### `Logger.Warning`

Logs warning messages. Useful for potentially harmful situations.

```csharp
Logger.Warning("This is a warning message");
Logger.Warning("Potential issue with event type: {EventType}", eventType);
```

### `Logger.Fatal`

Logs fatal messages. Useful for critical issues that cause the application to crash.

```csharp
Logger.Fatal("This is a fatal message");
Logger.Fatal(ex, "A fatal error occurred with event type: {EventType}", eventType);
```

These logging methods can be used within your `.rule` files to log different levels of information, helping you to monitor and debug the execution of your scripts effectively.

## Client Functions
Your `Client` object provides the following functions:

### `GetWorkItem`
```csharp
public async Task<WorkItem> GetWorkItem(string workItemId)
```
This function retrieves a work item from Azure DevOps based on the provided work item ID.

---

#### Returned Class: `WorkItem`

The `WorkItem` class represents a work item retrieved from Azure DevOps.

##### Properties

- `Id`: The unique identifier of the work item.
- `Revision`: The revision number of the work item.
- `Fields`: A dictionary containing the fields of the work item

.
  - **Description**: Represents the fields and their corresponding values of the work item.
- `Title`: The title of the work item.
  - **Description**: Represents the title of the work item.
- `WorkItemType`: The type of the work item.
  - **Description**: Represents the type of the work item.
- `State`: The state of the work item.
  - **Description**: Represents the current state of the work item.
- `Project`: The project associated with the work item.
  - **Description**: Represents the project to which the work item belongs.
- `Parent`: The parent work item relation.
  - **Description**: Represents the parent work item relation if applicable.
- `Children`: The list of child work item relations.
  - **Description**: Represents the list of child work item relations if applicable.
- `Relations`: The list of work item relations.
  - **Description**: Represents the list of work item relations associated with the work item.

---

### `SaveWorkItem`
```csharp
public async Task<bool> SaveWorkItem(WorkItem newWorkItem)
```
This function saves a new or updated work item to Azure DevOps.

#### `GetAllTeamIterations`

```csharp
public async Task<List<IterationDetails>> GetAllTeamIterations(string teamName)
```

This method retrieves all iterations for a specified team in Azure DevOps.

#### Returned Class: `IterationDetails`

The `IterationDetails` class represents details of an iteration in Azure DevOps.

##### Properties

- `Id`: The unique identifier of the iteration.
  - **Description**: Represents the unique identifier assigned to the iteration.
- `Name`: The name of the iteration.
  - **Description**: Represents the name of the iteration.
- `Path`: The path of the iteration.
  - **Description**: Represents the path of the iteration within the project hierarchy.
- `Attributes`: The attributes of the iteration.
  - **Description**: Represents additional attributes associated with the iteration.
- `Url`: The URL of the iteration.
  - **Description**: Represents the URL that can be used to access detailed information about the iteration.

#### Subclass: `IterationAttributes`

The `IterationAttributes` class represents the start and finish dates of an iteration.

##### Properties

- `StartDate`: The start date of the iteration.
  - **Description**: Represents the date when the iteration starts.
- `FinishDate`: The finish date of the iteration.
  - **Description**: Represents the date when the iteration finishes.

#### `GetTeamsIterationDetailsByName`

```csharp
public async Task<IterationDetails> GetTeamsIterationDetailsByName(string teamName, string iterationName)
```

This method retrieves details of a specific iteration for a given team in Azure DevOps.

# Scheduled Scripts

The ADO Automation Tool supports executing C# scripts on a timer schedule with **individual script intervals** and **last run tracking**. Each script can define its own execution frequency and access information about when it was last executed, enabling powerful incremental data processing scenarios.

## Features

- **Per-Script Intervals**: Each script can define its own execution interval
- **Last Run Tracking**: Scripts have access to their last execution timestamp for incremental processing
- **Configurable Default Last Run**: Set default last run date for first executions after system restart
- **Backward Compatibility**: Existing scripts continue to work with global interval
- **Intelligent Scheduling**: Only scripts that are due for execution are run
- **Memory Tracking**: Last run times are tracked in memory for each script
- **Flexible Return Values**: Scripts can return success, failure, and next interval information
- **Separate Timeouts**: Scheduled scripts can have longer timeouts than webhook scripts

## Scheduled Scripts Configuration

### Settings

Add the following settings to your `appsettings.json` file:

```json
{
  "Settings": {
    "ScheduledTaskIntervalMinutes": 1,
    "ScheduledScriptDefaultLastRun": "7",
    "ScheduledScriptExecutionTimeoutSeconds": 3600
  }
}
```
- `ScheduledTaskIntervalMinutes`: How often the service checks for scripts to execute (recommended: 1 minute for fine-grained control)
- `ScheduledScriptDefaultLastRun`: Default last run date for scripts on first execution after system restart
  - Can be an ISO date string: `"2024-01-01T00:00:00Z"`
  - Can be number of days ago: `"7"` (7 days ago)
  - If not set, defaults to current time
- `ScheduledScriptExecutionTimeoutSeconds`: Timeout in seconds for scheduled script execution (default: 3600 seconds / 1 hour)

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

### Available Context in Scheduled Scripts

In scheduled scripts, you have access to:

- `Client`: IAzureDevOpsClient - Azure DevOps client for API operations
- `Logger`: ILogger - Serilog logger for output
- `LastRun`: DateTime - When this script was last executed
- `EventType`: String - Always "ScheduledTask" for scheduled executions
- `Project`: String - Can be set by scripts if needed
- `Self`: WorkItem - A placeholder work item (ID = 0)
- `CancellationToken`: CancellationToken - For handling timeouts (respects ScheduledScriptExecutionTimeoutSeconds)

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

## Scheduled Scripts Logging

The service provides detailed logging including last run information:
- When each script is due for execution
- Last run timestamp for each script
- Execution start/completion times
- Script execution duration with timeout information
- Next scheduled execution time for each script
- Any errors or warnings including timeout errors

Example log output:
```
[INFO] Executing 3 of 5 scheduled scripts (timeout: 3600s)
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
- Uses separate timeout configuration for scheduled scripts (longer than webhook scripts)

## Configuration Examples

### Complete Configuration
```json
{
  "Settings": {
    "ScheduledTaskIntervalMinutes": 1,
    "ScheduledScriptDefaultLastRun": "7",
    "ScheduledScriptExecutionTimeoutSeconds": 3600
  }
}
```

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
- Scheduled scripts have longer default timeouts (1 hour) to accommodate complex processing

## Migration Guide

### From Global Intervals to Per-Script Intervals with LastRun

1. **Existing scripts** continue to work without changes and now have access to LastRun
2. **To add custom intervals**: Add a return statement with `ScheduledScriptResult.Success(intervalMinutes)`
3. **To use LastRun**: Access the `LastRun` variable for incremental processing
4. **Recommended**: Update `ScheduledTaskIntervalMinutes` to 1 minute in configuration
5. **Configure**: Set `ScheduledScriptDefaultLastRun` for appropriate default behavior
6. **Configure timeouts**: Set `ScheduledScriptExecutionTimeoutSeconds` for longer running scripts
7. **Test**: Verify scripts run at expected intervals and process incremental data correctly

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


