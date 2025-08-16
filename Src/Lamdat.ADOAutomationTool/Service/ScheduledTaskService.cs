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
    /// Now supports per-script intervals with scripts defining their own execution frequency.
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

            // Use a more frequent check interval (1 minute) to support fine-grained script intervals
            // The scripts themselves determine when they should run based on their individual intervals
            var checkIntervalMinutes = Math.Min(_settings.ScheduledTaskIntervalMinutes, 1.0);
            var intervalInMilliseconds = checkIntervalMinutes * 60 * 1000;
            
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
                _logger.Information($"Scheduled Task Service started. Will check for scripts to execute every {_timer.Interval / 60000:F1} minutes.");
                _logger.Information("Scripts can now define their own execution intervals. The service will execute scripts when their individual intervals have elapsed.");
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
                _logger.Debug($"Scheduled task check started at {DateTime.Now}");
                await ExecuteScheduledTasks();
                _logger.Debug($"Scheduled task check completed at {DateTime.Now}");
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
        /// Executes scheduled scripts that are due for execution based on their individual intervals.
        /// Scripts can define their own execution intervals by returning a ScheduledScriptResult.
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

                // Create a context for scheduled execution with default interval information
                var context = CreateScheduledContext();
                
                // Execute the scripts using the specialized scheduled script engine
                // The engine will determine which scripts need to run based on their intervals
                var result = await _scheduledScriptEngine.ExecuteScheduledScripts(context);
                
                if (!string.IsNullOrEmpty(result))
                {
                    _logger.Warning($"Scheduled script execution returned errors: {result}");
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

        /// <summary>
        /// Gets information about all scheduled scripts and their next execution times
        /// </summary>
        public Dictionary<string, ScheduledScriptInfo> GetScheduleInfo()
        {
            return _scheduledScriptEngine.GetScheduleInfo();
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