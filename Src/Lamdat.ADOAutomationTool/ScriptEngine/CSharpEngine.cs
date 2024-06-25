using Lamdat.ADOAutomationTool.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Lamdat.ADOAutomationTool.ScriptEngine
{
    public class CSharpScriptEngine
    {
        private readonly Serilog.ILogger _logger;
        private const int MAX_ATTEMPTS = 3;
        private readonly object _lock = new object();

        public CSharpScriptEngine(Serilog.ILogger logger)
        {
            _logger = logger;
        }

        public async Task<string> ExecuteScripts(Context context)
        {
            var errCol = new ConcurrentDictionary<string, string>();
            string err = null;

            try
            {
                string scriptsDirectory = "scripts";
                if (!Directory.Exists(scriptsDirectory))
                {
                    _logger.Warning("Scripts Directory not found");
                    return null;
                }

                string[] scriptFiles = Directory.GetFiles(scriptsDirectory, "*.rule");
                string[] orderedScriptFiles = scriptFiles.OrderBy(f => Path.GetFileName(f)).ToArray();

                //var parallelOptions = new ParallelOptions
                //{
                //    MaxDegreeOfParallelism = 1
                //};

                foreach (var scriptFile in orderedScriptFiles)
                {
                    var attempts = 1;
                    var succeeded = false;
                    while (!succeeded && attempts <= MAX_ATTEMPTS)
                    {
                        try
                        {
                            LogExecutionAttempt(context, scriptFile, attempts);

                            attempts++;
                            string scriptCode;

                            // Ensure file stream is disposed of properly
                            using (var fileStream = new FileStream(scriptFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                            using (var reader = new StreamReader(fileStream))
                            {
                                scriptCode = await reader.ReadToEndAsync();
                            }

                            ScriptOptions options = ScriptOptions.Default
                                .AddReferences(typeof(WebHookInfo<WebHookResourceUpdate>).Assembly)
                                .AddImports("Lamdat.ADOAutomationTool.Entities")
                                .AddImports("Microsoft.Extensions.Logging")
                                .AddImports("System")
                                .AddImports("System.Linq")
                                .AddImports("System.Threading.Tasks");

                            context.Self = await context.Client.GetWorkItem(context.Self.Id);  // refresh the entity if it was updated before

                            // Use _lock to ensure thread safety when accessing shared resources
                            lock (_lock)
                            {
                                var compiledScript = CSharpScript.Create(scriptCode, options, globalsType: context.GetType());
                                compiledScript.RunAsync(globals: context).Wait();

                                context.Client.SaveWorkItem(context.Self, attempts == MAX_ATTEMPTS).Wait();
                            }

                            succeeded = true;
                        }
                        catch (CompilationErrorException ex)
                        {
                            HandleScriptError(errCol, scriptFile, attempts, ex, "Script compilation error");
                            if (attempts == MAX_ATTEMPTS) succeeded = true;
                        }
                        catch (Exception ex)
                        {
                            HandleScriptError(errCol, scriptFile, attempts, ex, "Error executing script");
                            if (attempts == MAX_ATTEMPTS) succeeded = true;
                        }
                    }
                }

                _logger.Debug("Done Executing all scripts");
                if (errCol.Count > 0)
                {
                    err = string.Join(Environment.NewLine, errCol.Select(kv => $"location: {kv.Key}, error: {kv.Value}"));
                }
            }
            catch (Exception ex)
            {
                var erro = $"Error executing scripts: {ex.Message}";
                _logger.Error(erro);
                errCol.GetOrAdd("Error", erro);
            }

            return err;
        }

        private void LogExecutionAttempt(Context context, string scriptFile, int attempts)
        {
            if (attempts == 1)
                _logger.Information($"**** Event:'{context.EventType}'; Workitem type:'{context.Self.WorkItemType}',Workitem Id: {context.Self.Id}, executing script {scriptFile} ****");
            else
                _logger.Information($"** Attempt {attempts}, Event '{context.EventType}', executing script {scriptFile} **");
        }

        private void HandleScriptError(ConcurrentDictionary<string, string> errCol, string scriptFile, int attempts, Exception ex, string errorMessage)
        {
            string error = $"{errorMessage} in file '{scriptFile}': {ex.Message}";
            if (attempts < MAX_ATTEMPTS)
                _logger.Warning($"Attempt {attempts} failed with an error: {error}, will retry");
            else
                _logger.Error(error);
            errCol.GetOrAdd(scriptFile, error);
        }
    }
}
