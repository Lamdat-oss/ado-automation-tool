# Lamdat.Aggregation.Scripts

This console application demonstrates how to use the ADO Automation Tool entities and services from a standalone application with configuration support.

## Features

- Reads configuration from `appsettings.json`
- Creates and configures a `Settings` object from the configuration
- Sets up Serilog logging with both console and file outputs
- Creates an Azure DevOps client with the configured settings
- Executes the aggregation script runner
- Validates connection to Azure DevOps before running scripts

## Configuration

The application reads settings from `appsettings.json`. You need to update the following settings:

### Required Settings
- `CollectionURL`: Your Azure DevOps collection URL
- `PAT`: Your Personal Access Token for Azure DevOps

### Example Configuration
```json
{
  "Settings": {
    "CollectionURL": "https://dev.azure.com/yourorg",
    "PAT": "your-personal-access-token-here",
    "BypassRules": true,
    "ScriptExecutionTimeoutSeconds": 600,
    "SharedKey": "your-shared-key-here"
  }
}
```

## Usage

1. Update the `appsettings.json` file with your Azure DevOps settings
2. Build and run the application:
   ```
   dotnet build
   dotnet run
   ```

## Dependencies

- `Microsoft.Extensions.Configuration` - For reading JSON configuration
- `Microsoft.Extensions.Configuration.Json` - For JSON file support
- `Microsoft.Extensions.Configuration.Binder` - For binding configuration to objects
- `Lamdat.ADOAutomationTool` - The main automation tool project (via project reference)

## Logging

Logs are written to:
- Console (with formatted output)
- File: `./logs/logfile.log` (daily rolling)

## Notes

- The application includes connection validation before running scripts
- Default timeout is set to 600 seconds (10 minutes) for script execution
- The application will exit with error messages if required configuration is missing