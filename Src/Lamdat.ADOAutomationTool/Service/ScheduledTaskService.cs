using System;
using System.IO;
using System.Linq;
using System.Timers;
using System.Threading.Tasks;
using System.Collections.Generic;
using Lamdat.ADOAutomationTool.ScriptEngine;
using Lamdat.ADOAutomationTool.Entities;

namespace Lamdat.ADOAutomationTool.Service
{
    /// <summary>
    /// Service for executing scheduled tasks at regular intervals using a timer.
    /// This service executes C# scripts from a designated scheduled-scripts directory.
    /// </summary>
    public class ScheduledTaskService : IScheduledTaskService, IDisposable
    {
        private readonly System.Timers.Timer _timer;
        private readonly Serilog.ILogger _logger;
        private readonly ScheduledScriptEngine _scheduledScriptEngine;
        private readonly Settings _settings;
        private readonly IAzureDevOpsClient _azureDevOpsClient;
        private bool _isExecuting;
        private bool _disposed;

        public bool IsRunning => _timer?.Enabled ?? false;

        public ScheduledTaskService(
            Serilog.ILogger logger, 
            ScheduledScriptEngine scheduledScriptEngine, 
            Settings settings,
            IAzureDevOpsClient azureDevOpsClient)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _scheduledScriptEngine = scheduledScriptEngine ?? throw new ArgumentNullException(nameof(scheduledScriptEngine));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _azureDevOpsClient = azureDevOpsClient ?? throw new ArgumentNullException(nameof(azureDevOpsClient));

            // Convert minutes to milliseconds
            var intervalInMilliseconds = _settings.ScheduledTaskIntervalMinutes * 60 * 1000;
            _timer = new System.Timers.Timer(intervalInMilliseconds);
            _timer.Elapsed += OnTimedEvent;
            _timer.AutoReset = true;
        }

        public void Start()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ScheduledTaskService));

            if (!_timer.Enabled)
            {
                _timer.Start();
                _logger.Information($"Scheduled Task Service started. Will execute every {_settings.ScheduledTaskIntervalMinutes} minutes.");
            }
        }

        public void Stop()
        {
            if (_timer.Enabled)
            {
                _timer.Stop();
                _logger.Information("Scheduled Task Service stopped.");
            }
        }

        private async void OnTimedEvent(object source, ElapsedEventArgs e)
        {
            if (_isExecuting)
            {
                _logger.Debug("Scheduled task execution already in progress, skipping this interval.");
                return;
            }

            _isExecuting = true;
            try
            {
                _logger.Debug($"Scheduled task execution started at {DateTime.Now}");
                await ExecuteScheduledTasks();
                _logger.Debug($"Scheduled task execution completed at {DateTime.Now}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error occurred during scheduled task execution");
            }
            finally
            {
                _isExecuting = false;
            }
        }

        /// <summary>
        /// Executes all scheduled scripts from the scheduled-scripts directory.
        /// Scripts should have .rule extension to be executed.
        /// </summary>
        private async Task ExecuteScheduledTasks()
        {
            try
            {
                string scheduledScriptsDirectory = "scheduled-scripts";
                if (!Directory.Exists(scheduledScriptsDirectory))
                {
                    _logger.Debug("Scheduled scripts directory not found, creating it.");
                    Directory.CreateDirectory(scheduledScriptsDirectory);
                    return;
                }

                string[] scriptFiles = Directory.GetFiles(scheduledScriptsDirectory, "*.rule");
                if (scriptFiles.Length == 0)
                {
                    _logger.Debug("No scheduled scripts found to execute.");
                    return;
                }

                _logger.Information($"Found {scriptFiles.Length} scheduled scripts to execute.");

                // Create a context for scheduled execution
                var context = CreateScheduledContext();
                
                // Execute the scripts using the specialized scheduled script engine
                var result = await _scheduledScriptEngine.ExecuteScheduledScripts(context);
                
                if (!string.IsNullOrEmpty(result))
                {
                    _logger.Warning($"Scheduled script execution returned errors: {result}");
                }
                else
                {
                    _logger.Information("All scheduled scripts executed successfully.");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in ExecuteScheduledTasks");
            }
        }

        /// <summary>
        /// Creates a context for scheduled task execution.
        /// This creates a minimal context suitable for scheduled script execution.
        /// </summary>
        private IContext CreateScheduledContext()
        {
            return new Context(_azureDevOpsClient, _logger)
            {
                ScriptExecutionTimeoutSeconds = _settings.ScriptExecutionTimeoutSeconds,
                EventType = "ScheduledTask",
                Project = null, // Can be set by scripts if needed
                RelationChanges = new Relations(),
                Self = new WorkItem 
                { 
                    Id = 0, 
                    Title = "Scheduled Task Execution", 
                    Fields = new Dictionary<string, object?>
                    {
                        ["System.WorkItemType"] = "ScheduledTask"
                    }
                },
                SelfChanges = new Dictionary<string, object>(),
                WebHookResource = new WebHookResourceUpdate(),
                ScriptRunId = Guid.NewGuid().ToString("N")[..8] // Short run ID for scheduled tasks
            };
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            Stop();
            _timer?.Dispose();
            _disposed = true;
        }
    }
}