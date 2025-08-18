// See https://aka.ms/new-console-template for more information
using Lamdat.ADOAutomationTool.Entities;
using Lamdat.ADOAutomationTool.Service;
using Lamdat.Aggregation.Scripts;
using Microsoft.Extensions.Configuration;
using Serilog;

Console.WriteLine("Starting Aggregation Scripts...");

/// Build configuration
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}]: {Level:u4} | {Message:l}{NewLine}{Exception}")
    .WriteTo.File($"{AppDomain.CurrentDomain.BaseDirectory}/logs/logfile.log", rollingInterval: RollingInterval.Day)
    .CreateBootstrapLogger();

// Get Settings from configuration
var settings = new Settings();
configuration.GetSection("Settings").Bind(settings);

if (settings == null)
{
    Log.Error("Failed to load settings from appsettings.json");
    return;
}

// Validate required settings
if (string.IsNullOrWhiteSpace(settings.CollectionURL))
{
    Log.Error("CollectionURL is required in settings");
    return;
}

if (string.IsNullOrWhiteSpace(settings.PAT))
{
    Log.Error("PAT (Personal Access Token) is required in settings");
    return;
}

Log.Information("Configuration loaded successfully");
Log.Information($"Azure DevOps Collection URL: {settings.CollectionURL}");
Log.Information($"Bypass Rules: {settings.BypassRules}");
Log.Information($"Script Execution Timeout: {settings.ScriptExecutionTimeoutSeconds} seconds");

/// Create Azure DevOps client
var client = new AzureDevOpsClient(Log.Logger, settings.CollectionURL, settings.PAT, settings.BypassRules, settings.NotValidCertificates);

try
{
    // Test connection
    var user = await client.WhoAmI();
    if (user?.Identity?.DisplayName != null)
    {
        Log.Information($"Connected to Azure DevOps as: {user.Identity.DisplayName}");
    }
    else
    {
        Log.Error("Failed to connect to Azure DevOps - invalid credentials or URL");
        return;
    }

    // Run aggregation script
    Log.Information("Starting aggregation script execution...");
    var scriptRunId = Guid.NewGuid().ToString();
    var lastRun = DateTime.Now.AddDays(-7); // Default to 7 days ago
    
    using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(settings.ScriptExecutionTimeoutSeconds));
    
    var result = await AggregationScriptRunner.Run(client, Log.Logger, cancellationTokenSource.Token, scriptRunId, lastRun);
    
    if (result.IsSuccess)
    {
        Log.Information($"Aggregation script completed successfully: {result.Message}");
        if (result.NextExecutionIntervalMinutes.HasValue)
        {
            Log.Information($"Next execution suggested in {result.NextExecutionIntervalMinutes.Value} minutes");
        }
    }
    else
    {
        Log.Error($"Aggregation script failed: {result.Message}");
    }
}
catch (Exception ex)
{
    Log.Error(ex, "An error occurred during script execution");
}
finally
{
    Log.CloseAndFlush();
}

Console.WriteLine("Aggregation Scripts completed. Press any key to exit...");
Console.ReadKey();



