using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Lamdat.ADOAutomationTool.Entities;
using csscript;
using CSScriptLib;

namespace Lamdat.ADOAutomationTool.ScriptEngine
{
    /// <summary>
    /// Specialized script engine for executing scheduled tasks.
    /// This extends the functionality to handle scripts from the scheduled-scripts directory.
    /// </summary>
    public class ScheduledScriptEngine
    {
        private readonly Serilog.ILogger _logger;
        private readonly Lamdat.ADOAutomationTool.Entities.Settings _settings;
        private const int MAX_ATTEMPTS = 3;
        private readonly object _lock = new object();
        
        // Dictionary to track script execution intervals and last run times
        private readonly ConcurrentDictionary<string, ScheduledScriptInfo> _scriptScheduleInfo = new();

        public ScheduledScriptEngine(Serilog.ILogger logger, Lamdat.ADOAutomationTool.Entities.Settings settings)
        {
            _logger = logger;
            _settings = settings;
            CSScript.EvaluatorConfig.Engine = EvaluatorEngine.Roslyn;
        }

        /// <summary>
        /// Executes scheduled scripts from the scheduled-scripts directory based on their individual intervals.
        /// </summary>
        public async Task<string> ExecuteScheduledScripts(IContext context)
        {
            var errCol = new ConcurrentDictionary<string, string>();
            string err = null;

            // Read timeout from configuration, fallback to 60 seconds if not set or invalid
            int timeoutSeconds = 600;
            if (context.ScriptExecutionTimeoutSeconds > 0)
            {
                timeoutSeconds = context.ScriptExecutionTimeoutSeconds;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            var token = cts.Token;

            try
            {
                string scriptsDirectory = "scheduled-scripts";
                if (!Directory.Exists(scriptsDirectory))
                {
                    _logger.Warning("Scheduled Scripts Directory not found");
                    return null;
                }

                string[] scriptFiles = Directory.GetFiles(scriptsDirectory, "*.rule");
                string[] orderedScriptFiles = scriptFiles.OrderBy(f => Path.GetFileName(f)).ToArray();

                var scriptsToExecute = GetScriptsToExecute(orderedScriptFiles, context);
                
                if (scriptsToExecute.Count == 0)
                {
                    _logger.Debug("No scheduled scripts need to be executed at this time");
                    return null;
                }

                _logger.Information($"Executing {scriptsToExecute.Count} of {orderedScriptFiles.Length} scheduled scripts");

                foreach (var scriptFile in scriptsToExecute)
                {
                    var attempts = 1;
                    var succeeded = false;
                    while (!succeeded && attempts <= MAX_ATTEMPTS)
                    {
                        try
                        {
                            token.ThrowIfCancellationRequested();

                            LogExecutionAttempt(context, scriptFile, attempts);
                            attempts++;

                            string scriptCode;
                            using (var fileStream = new FileStream(scriptFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                            using (var reader = new StreamReader(fileStream))
                            {
                                scriptCode = await reader.ReadToEndAsync(token);
                            }

                            var lastRun = GetLastRunForScript(scriptFile);
                            var result = await ExecuteScript(scriptCode, scriptFile, context, token, lastRun);
                            
                            // Update the script schedule information based on the result
                            UpdateScriptScheduleInfo(scriptFile, result, context, lastRun);

                            succeeded = true;
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.Error($"Scheduled Script '{scriptFile}' execution cancelled due to timeout.");
                            errCol.GetOrAdd("Timeout", "Scheduled script execution cancelled due to timeout.");
                            return "Scheduled script execution cancelled due to timeout.";
                        }
                        catch (Exception ex)
                        {
                            HandleScriptError(errCol, scriptFile, attempts, ex, "Error executing scheduled script");
                            if (attempts == MAX_ATTEMPTS) succeeded = true;
                        }
                    }
                }

                _logger.Debug("Done Executing all scheduled scripts");
                if (errCol.Count > 0)
                {
                    err = string.Join(Environment.NewLine, errCol.Select(kv => $"location: {kv.Key}, error: {kv.Value}"));
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Error("Scheduled script execution cancelled due to timeout.");
                errCol.GetOrAdd("Timeout", "Scheduled script execution cancelled due to timeout.");
                return "Scheduled script execution cancelled due to timeout.";
            }
            catch (Exception ex)
            {
                var erro = $"Error executing scheduled scripts: {ex.Message}, {ex.StackTrace}";
                _logger.Error(erro);
                errCol.GetOrAdd("Error", erro);
            }

            return err;
        }

        /// <summary>
        /// Gets the last run date for a script, using configuration default for first runs
        /// </summary>
        private DateTime GetLastRunForScript(string scriptFile)
        {
            if (_scriptScheduleInfo.TryGetValue(scriptFile, out var scheduleInfo))
            {
                return scheduleInfo.LastExecuted;
            }

            // First run - use configured default
            return GetDefaultLastRunDate();
        }

        /// <summary>
        /// Gets the default last run date from configuration
        /// </summary>
        private DateTime GetDefaultLastRunDate()
        {
            if (string.IsNullOrWhiteSpace(_settings.ScheduledScriptDefaultLastRun))
            {
                return DateTime.Now;
            }

            var defaultLastRunConfig = _settings.ScheduledScriptDefaultLastRun.Trim();

            // Try to parse as ISO date first
            if (DateTime.TryParse(defaultLastRunConfig, null, DateTimeStyles.RoundtripKind, out var dateTime))
            {
                return dateTime;
            }

            // Try to parse as number of days ago
            if (int.TryParse(defaultLastRunConfig, out var daysAgo))
            {
                return DateTime.Now.AddDays(-Math.Abs(daysAgo));
            }

            // If parsing fails, log warning and use current time
            _logger.Warning($"Invalid ScheduledScriptDefaultLastRun configuration: '{defaultLastRunConfig}'. Using current time.");
            return DateTime.Now;
        }

        /// <summary>
        /// Determines which scripts should be executed based on their individual intervals
        /// </summary>
        private List<string> GetScriptsToExecute(string[] allScriptFiles, IContext context)
        {
            var scriptsToExecute = new List<string>();
            var defaultIntervalMinutes = GetDefaultIntervalMinutes(context);

            foreach (var scriptFile in allScriptFiles)
            {
                if (!_scriptScheduleInfo.TryGetValue(scriptFile, out var scheduleInfo))
                {
                    // First time running this script, add it to execute list
                    scriptsToExecute.Add(scriptFile);
                    _logger.Debug($"First execution for script '{scriptFile}' (Last run will be: {GetDefaultLastRunDate():yyyy-MM-dd HH:mm:ss})");
                }
                else if (scheduleInfo.ShouldExecuteNow)
                {
                    scriptsToExecute.Add(scriptFile);
                    _logger.Debug($"Script '{scriptFile}' scheduled for execution (interval: {scheduleInfo.IntervalMinutes} min, last run: {scheduleInfo.LastExecuted})");
                }
                else
                {
                    _logger.Debug($"Script '{scriptFile}' not due for execution (next run: {scheduleInfo.NextScheduledExecution})");
                }
            }

            return scriptsToExecute;
        }

        /// <summary>
        /// Executes a single script and returns the result
        /// </summary>
        private async Task<ScheduledScriptResult> ExecuteScript(string scriptCode, string scriptFile, IContext context, CancellationToken token, DateTime lastRun)
        {
            // First try to execute as an interval-aware script
            try
            {
                var intervalScript = await CreateIntervalScript(scriptCode, context, token);
                if (intervalScript != null)
                {
                    return await intervalScript.RunWithInterval(context.Client, context.Logger, token, context.ScriptRunId, lastRun);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Script '{scriptFile}' does not implement interval interface, falling back to standard execution: {ex.Message}");
            }

            // Fallback to standard scheduled script execution
            var script = await CreateStandardScript(scriptCode, context, token);
            await script.Run(context.Client, context.Logger, token, context.ScriptRunId, lastRun);
            
            // Return a default success result
            return ScheduledScriptResult.Success();
        }

        /// <summary>
        /// Creates an interval-aware script instance
        /// </summary>
        private async Task<IScheduledScriptWithInterval> CreateIntervalScript(string scriptCode, IContext context, CancellationToken token)
        {
            // Build the script wrapper for interval-aware scheduled tasks
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.AppendLine(@"
using Lamdat.ADOAutomationTool.Entities;
using Lamdat.ADOAutomationTool.Service;
using Lamdat.ADOAutomationTool.ScriptEngine;
using Serilog;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System;

public async Task<ScheduledScriptResult> RunWithInterval(IAzureDevOpsClient Client, ILogger Logger, CancellationToken cancellationToken, string ScriptRunId, DateTime LastRun)
{");
            stringBuilder.AppendLine(scriptCode);
            stringBuilder.AppendLine("}");
            
            var wrappedCode = stringBuilder.ToString();

            IScheduledScriptWithInterval script;
            lock (_lock)
            {
                script = CSScript.Evaluator.LoadMethod<IScheduledScriptWithInterval>(wrappedCode);
            }

            return script;
        }

        /// <summary>
        /// Creates a standard script instance
        /// </summary>
        private async Task<IScheduledScript> CreateStandardScript(string scriptCode, IContext context, CancellationToken token)
        {
            // Build the script wrapper for standard scheduled tasks
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.AppendLine(@"
using Lamdat.ADOAutomationTool.Entities;
using Lamdat.ADOAutomationTool.Service;
using Serilog;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System;

public async Task Run(IAzureDevOpsClient Client, ILogger Logger, CancellationToken cancellationToken, string ScriptRunId, DateTime LastRun)
{");
            stringBuilder.AppendLine(scriptCode);
            stringBuilder.AppendLine("}");
            
            var wrappedCode = stringBuilder.ToString();

            IScheduledScript script;
            lock (_lock)
            {
                script = CSScript.Evaluator.LoadMethod<IScheduledScript>(wrappedCode);
            }

            return script;
        }

        /// <summary>
        /// Updates the schedule information for a script based on execution result
        /// </summary>
        private void UpdateScriptScheduleInfo(string scriptFile, ScheduledScriptResult result, IContext context, DateTime previousLastRun)
        {
            var intervalMinutes = result.NextExecutionIntervalMinutes ?? GetDefaultIntervalMinutes(context);
            var currentTime = DateTime.Now;
            
            _scriptScheduleInfo.AddOrUpdate(scriptFile,
                new ScheduledScriptInfo
                {
                    ScriptPath = scriptFile,
                    LastExecuted = currentTime,
                    PreviousLastExecuted = previousLastRun,
                    IntervalMinutes = intervalMinutes,
                    IsFirstRun = true
                },
                (key, existing) =>
                {
                    existing.PreviousLastExecuted = existing.LastExecuted;
                    existing.LastExecuted = currentTime;
                    existing.IntervalMinutes = intervalMinutes;
                    existing.IsFirstRun = false;
                    return existing;
                });

            _logger.Information($"Script '{scriptFile}' completed. Last run: {previousLastRun:yyyy-MM-dd HH:mm:ss}, Next execution in {intervalMinutes} minutes at {currentTime.AddMinutes(intervalMinutes):yyyy-MM-dd HH:mm:ss}");
        }

        /// <summary>
        /// Gets the default interval from context or falls back to 5 minutes
        /// </summary>
        private int GetDefaultIntervalMinutes(IContext context)
        {
            // Use configured interval from settings
            return (int)_settings.ScheduledTaskIntervalMinutes;
        }

        private void LogExecutionAttempt(IContext context, string scriptFile, int attempts)
        {
            if (attempts == 1)
            {
                _logger.Information($"----------------------------------------------------");
                _logger.Information(
                    $"**** Scheduled Task Event:'{context.EventType}'; executing script {scriptFile}. (Run ID {context.ScriptRunId}) ****");
            }
            else
                _logger.Information($"** Scheduled Task Attempt {attempts}, executing script {scriptFile}. (Run ID {context.ScriptRunId}) **");
        }

        private void HandleScriptError(ConcurrentDictionary<string, string> errCol, string scriptFile, int attempts, Exception ex, string errorMessage)
        {
            string error = $"{errorMessage} in file '{scriptFile}': {ex.Message}";
            if (attempts < MAX_ATTEMPTS)
                _logger.Warning($"Scheduled Task Attempt {attempts} failed with an error: {error}, will retry");
            else
                _logger.Error(error);
            errCol.GetOrAdd(scriptFile, error);
        }

        /// <summary>
        /// Gets information about all scheduled scripts and their next execution times
        /// </summary>
        public Dictionary<string, ScheduledScriptInfo> GetScheduleInfo()
        {
            return new Dictionary<string, ScheduledScriptInfo>(_scriptScheduleInfo);
        }
    }
}