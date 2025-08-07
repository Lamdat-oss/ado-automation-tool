using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Diagnostics;
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
        private const int MAX_ATTEMPTS = 3;
        private readonly object _lock = new object();

        public ScheduledScriptEngine(Serilog.ILogger logger)
        {
            _logger = logger;
            CSScript.EvaluatorConfig.Engine = EvaluatorEngine.Roslyn;
        }

        /// <summary>
        /// Executes scheduled scripts from the scheduled-scripts directory.
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

                foreach (var scriptFile in orderedScriptFiles)
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

                            // Build the script wrapper for scheduled tasks
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

            public async Task Run(IAzureDevOpsClient Client, ILogger Logger, CancellationToken cancellationToken, string ScriptRunId)
            {");
                            stringBuilder.AppendLine(scriptCode);
                            stringBuilder.AppendLine("}");
                            scriptCode = stringBuilder.ToString();

                            IScheduledScript script;
                            lock (_lock)
                            {
                                script = CSScript.Evaluator.LoadMethod<IScheduledScript>(scriptCode);
                            }

                            Stopwatch stopWatch = new Stopwatch();
                            try
                            {
                                stopWatch.Start();

                                await script.Run(context.Client, context.Logger, token, context.ScriptRunId);

                                
                            }
                            finally
                            {
                                script = null;

                                stopWatch.Stop();
                                TimeSpan ts = stopWatch.Elapsed;
                                string elapsedTime = String.Format("{0:00} min {1:00} sec {2:000} ms",
                                    ts.Minutes, ts.Seconds, ts.Milliseconds / 10);
                                _logger.Information($"                       ----");
                                _logger.Information($"Scheduled Script '{scriptFile}' execution time on attempt {attempts - 1}: {elapsedTime}. (Run ID {context.ScriptRunId})");
                            }

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
    }
}