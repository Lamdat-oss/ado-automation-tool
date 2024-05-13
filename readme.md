# ADO Automation Tool

The **ADO Automation Tool** is a web API designed to listen to webhooks from Azure DevOps. When a webhook is received, it triggers a script defined in the `scripts` folder with the `.rule` file suffix.

## Configuration

The tool can be configured using either a JSON configuration file, command line arguments, or environment variables.

### JSON Configuration

To use JSON configuration, create a `config.json` file with the following structure:

```json
{
  "Settings": {
    "CollectionURL": "",
    "PAT": "",
    "BypassRules": true,
    "SharedKey": ""
  }
}
```

### Command Line Arguments

You can also specify configuration settings through command line arguments when running the application. Here's how you can do it:

```bash
docker run --rm -it  -v .\Examples:/app/scripts   -p 5000:5000/tcp   -e "SETTINGS__COLLECTIONURL=https:///<azure-devops-host>/<collection> | dev.azure.com>/<org>" -e  "SETTINGS__PAT=<PAT>" -e "SETTINGS__BYPASSRULES=true" -e "SETTINGS__SHAREDKEY=<key>" adoautomationtool/adoautomationtool:latest
```

### Environment Variables

Alternatively, you can use environment variables to configure the tool. Set the following environment variables:

- `ADO_COLLECTION_URL`: URL of the Azure DevOps collection.
- `ADO_PAT`: Personal Access Token (PAT) used for authentication.
- `ADO_BYPASS_RULES`: Boolean value indicating whether to bypass Azure DevOps rules.
- `ADO_SHARED_KEY`: Key used to authenticate to the web service.

## Usage

1. Clone this repository to your local machine.
2. Configure the settings using one of the methods described above.
3. Deploy the application to your desired hosting environment.
4. Start the application.
5. Your ADO Automation Tool is now ready to receive webhooks and execute scripts.

## create pfx for tls using PowerShell
```$cert = New-SelfSignedCertificate -KeyLength 2048 -KeyAlgorithm RSA -Type SSLServerAuthentication -FriendlyName "adoAutomationTool" -NotAfter 2030-01-01 -Subject "adoautomationtool.example.com")
$certPass = Read-Host -Prompt "Password" -AsSecureString
Export-PfxCertificate -FilePath "adoautomation.pfx" -Cert $cert -Password $certPass
```

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
- `Logger`: Allows logging messages.
- `Client`: Provides access to Azure DevOps API for fetching additional work item details.

Example usage:
```csharp
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

### `SaveWorkItem`
```csharp
public async Task<bool> SaveWorkItem(WorkItem newWorkItem)
```
This function saves a new or updated work item to Azure DevOps.
