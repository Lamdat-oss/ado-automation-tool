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

        public async Task Start()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ScheduledTaskService));

            if (!_timer.Enabled)
            {
                _logger.Information($"Scheduled Task Service starting. Will check for scripts to execute every {_timer.Interval / 60000:F1} minutes.");
                _logger.Information("Scripts can now define their own execution intervals. The service will execute scripts when their individual intervals have elapsed.");

                // Execute scheduled tasks immediately on startup in background
                _logger.Information("Executing scheduled tasks immediately on startup (in background)...");

                // Use Task.Run to execute in background without blocking startup
                _ = Task.Run(async () =>
                {
                    if (!_isExecuting)
                    {
                        _isExecuting = true;
                        try
                        {
                            await ExecuteScheduledTasks();
                            _logger.Information("Initial scheduled tasks execution completed.");
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Error occurred during initial scheduled task execution on startup");
                        }
                        finally
                        {
                            _isExecuting = false;
                        }
                    }
                    else
                    {
                        _logger.Warning("Scheduled tasks execution already in progress during startup, skipping initial execution.");
                    }
                });

                // Start the timer for regular interval execution
                _timer.Start();
                _logger.Information("Scheduled Task Service started and timer enabled for regular intervals.");
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

                // Add better path resolution and logging for containerized environments
                var currentDirectory = Directory.GetCurrentDirectory();
                var fullScriptsPath = Path.Combine(currentDirectory, scheduledScriptsDirectory);

                _logger.Information($"Looking for scheduled scripts in: {fullScriptsPath}");
                _logger.Information($"Current working directory: {currentDirectory}");

                if (!Directory.Exists(scheduledScriptsDirectory))
                {
                    _logger.Warning($"Scheduled scripts directory '{fullScriptsPath}' not found, creating it.");
                    Directory.CreateDirectory(scheduledScriptsDirectory);

                    // Log what's actually in the current directory to help debug
                    var currentDirContents = Directory.GetFileSystemEntries(currentDirectory);
                    _logger.Information($"Current directory contains: {string.Join(", ", currentDirContents.Select(Path.GetFileName))}");
                    return;
                }

                string[] scriptFiles = Directory.GetFiles(scheduledScriptsDirectory, "*.rule");
                _logger.Information($"Found {scriptFiles.Length} script files in '{fullScriptsPath}'");

                if (scriptFiles.Length == 0)
                {
                    _logger.Warning("No scheduled scripts (.rule files) found to execute.");

                    // Log what files ARE in the directory to help debug
                    var allFiles = Directory.GetFiles(scheduledScriptsDirectory);
                    _logger.Information($"Directory contains {allFiles.Length} files: {string.Join(", ", allFiles.Select(Path.GetFileName))}");
                    return;
                }

                // Log the script files found
                _logger.Information($"Script files found: {string.Join(", ", scriptFiles.Select(Path.GetFileName))}");

                // Create a context for scheduled execution with default interval information
                var context = CreateScheduledContext();

                // Execute the scripts using the specialized scheduled script engine
                // The engine will determine which scripts need to run based on their intervals
                var result = await _scheduledScriptEngine.ExecuteScheduledScripts(context);

                if (!string.IsNullOrEmpty(result))
                {
                    _logger.Warning($"Scheduled script execution returned errors: {result}");
                }
                else
                {
                    _logger.Information("Scheduled script execution completed successfully.");
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
            var context = new Context(_azureDevOpsClient, _logger)
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

            return context;
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