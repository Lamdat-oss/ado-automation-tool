
# ADO Automation Tool

The **ADO Automation Tool** is a web API designed to listen to webhooks from Azure DevOps. When a webhook is received, it triggers a script defined in the `scripts` folder with the `.rule` file suffix.

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
    "BypassRules": true,
    "SharedKey": "",
    "AllowedCorsOrigin": "*",
    "NotValidCertificates": false,
    "EnableAutoHttpsRedirect": true
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
- `SETTINGS__SCRIPTEXECUTIONTIMEOUTSECONDS`: SCRIPT EXECUTION TIMEOUT IN SECONDS

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

# Rules language

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

