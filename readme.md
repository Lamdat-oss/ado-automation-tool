# ADO Automation Tool

The **ADO Automation Tool** is a web API designed to listen to webhooks from Azure DevOps. When a webhook is received, it triggers a script defined in the `scripts` folder with the `.rule` file suffix.

## Configuration

The tool can be configured using either a JSON configuration file, command line arguments, or environment variables.
Scripts/rules folder can be mounted into /app/scripts folder

### TLS configuration
1.  create pfx for tls using PowerShell
```$cert = New-SelfSignedCertificate -KeyLength 2048 -KeyAlgorithm RSA -Type SSLServerAuthentication -FriendlyName "adoAutomationTool" -NotAfter 2030-01-01 -Subject "adoautomationtool.example.com")
$certPass = Read-Host -Prompt "Password" -AsSecureString
Export-PfxCertificate -FilePath "adoautomation.pfx" -Cert $cert -Password $certPass
```
2. set the path of the pfx file to use in environment variable 'ASPNETCORE_Kestrel__Certificates__Default__Path', command line or the app settings file.
3. set the password for the pfx in environment variable 'ASPNETCORE_Kestrel__Certificates__Default__Password' command line or the app settings file.
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
    "NotValidCertificates": false
  }
}
```

### Command Line Arguments

You can also specify configuration settings through command line arguments when running the application. Here's how you can do it:

```bash
docker run --rm -it  -v ./Examples:/app/scripts   -p 5000:5000/tcp   -e "SETTINGS__COLLECTIONURL=https:///<azure-devops-host>/<collection> | dev.azure.com>/<org>" -e  "SETTINGS__PAT=<PAT>" -e "SETTINGS__BYPASSRULES=true" -e "SETTINGS__SHAREDKEY=<key>" adoautomationtool/adoautomationtool:latest

# with https
docker run -p 5000:5000/tcp  -p 5001:5001 --rm -it  -v ./Examples:/app/scripts -v ./adoautomation.pfx:/app/adoautomation.pfx -e -e ASPNETCORE_HTTPS_PORT=5001 -e ASPNETCORE_Kestrel__Certificates__Default__Password="***" -e ASPNETCORE_Kestrel__Certificates__Default__Path=/app/adoautomation.pfx  -e "SETTINGS__COLLECTIONURL=https://azuredevops.syncnow.io/NovaCollection" -e  "SETTINGS__PAT=****" -e "SETTINGS__BYPASSRULES=true" -e "SETTINGS__SHAREDKEY=***"   adoautomationtool/adoautomationtool:0.1.63

```

### Environment Variables

Alternatively, you can use environment variables to configure the tool. Set the following environment variables:

- `SETTINGS__COLLECTIONURL`: URL of the Azure DevOps collection.
- `SETTINGS__PAT`: Personal Access Token (PAT) used for authentication.
- `SETTINGS__BYPASSRULES`: Boolean value indicating whether to bypass Azure DevOps rules.
- `SETTINGS__SHAREDKEY`: Key used to authenticate to the web service.
- `SETTINGS__NOTVALIDCERTIFICATES`: If to allow working with not valid azure devops certificates

## Usage

1. Clone this repository to your local machine.
2. Configure the settings using one of the methods described above.
3. Deploy the application to your desired hosting environment.
4. Start the application.
5. Your ADO Automation Tool is now ready to receive webhooks and execute scripts.



# Rules language

## Example Rule
```csharp
Logger.Log(LogLevel.Information, $"Received event type: {EventType}, Work Item Id: {Self.Id}, Title: '{Self.Title}, State: {Self.State}, WorkItemType:  {Self.WorkItemType}");

Self.Fields["System.Description"] = $"Current Date: {DateTime.Today}";
if(Self.Parent != null){
    var parentWit = await Client.GetWorkItem(Self.Parent.RelatedWorkItemId);
    Logger.Log(LogLevel.Information, $"Work Item Parent Title: {parentWit.Title}");
    if(parentWit != null)
    {
        Logger.Log(LogLevel.Information, $"Work Item Parent Children Count: {parentWit.Children.Count}");
    }
}
```

## Usage of C# Context in .rule Files
In your `.rule` files, you'll have access to a C# context to interact with Azure DevOps objects. Here's how you can use it:

- `Self`: Represents the current work item being processed.
- `SelfChanges`: Represents the current work item changes. 
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
Logger.Log(LogLevel.Information, $"Work Item Title: {Self.Title}");

// Accessing parent work item
if(Self.Parent != null){
    var parentWit = await Client.GetWorkItem(Self.Parent.RelatedWorkItemId);
    Logger.Log(LogLevel.Information, $"Parent Work Item Title: {parentWit.Title}");
}
```

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
- `Fields`: A dictionary containing the fields of the work item.
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


