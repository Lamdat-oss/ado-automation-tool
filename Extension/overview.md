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
