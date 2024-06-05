using Lamdat.ADOAutomationTool.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;


namespace Lamdat.ADOAutomationTool.ScriptEngine
{
    public class CSharpScriptEngine
    {
        private readonly ILogger _logger;
        private const int MAX_ATTEMPTS = 3;

        public CSharpScriptEngine(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task<string> ExecuteScripts(Context context)
        {
            var errCol = new ConcurrentDictionary<string, string>();
            string err = null;

            try
            {
                string scriptsDirectory = "scripts";
                if (!Directory.Exists(scriptsDirectory))
                {
                    _logger.LogWarning("Scripts Directory not found");
                    return null;
                }
                string[] scriptFiles = Directory.GetFiles(scriptsDirectory, "*.rule");

                var parallelOptions = new ParallelOptions
                {
                    //MaxDegreeOfParallelism = Environment.ProcessorCount * 2 // we need to fix for optimistic concurrency
                    MaxDegreeOfParallelism = 1
                };

                Parallel.ForEach(scriptFiles, parallelOptions, async scriptFile =>
                {
                    var attempts = 1;
                    var succeeded = false;
                    while (!succeeded && attempts <= MAX_ATTEMPTS)
                    {
                        try
                        {
                            if (attempts == 1)
                                _logger.Log(LogLevel.Information, $"Event '{context.EventType}', executing script {scriptFile}");
                            else
                                _logger.Log(LogLevel.Information, $"Attempt {attempts.ToString()}, Event '{context.EventType}', executing script {scriptFile}");

                            attempts++;

                            string scriptCode = await File.ReadAllTextAsync(scriptFile);
                            ScriptOptions options = ScriptOptions.Default
                                .AddReferences(typeof(WebHookInfo).Assembly)
                                .AddImports("Lamdat.ADOAutomationTool.Entities")
                                .AddImports("Microsoft.Extensions.Logging")
                                .AddImports("System");
                            context.Self = await context.Client.GetWorkItem(context.Self.Id);  // refresh the entity if it was updated before

                            await CSharpScript.EvaluateAsync(scriptCode, options, globals: context);
                            var compiledScript = CSharpScript.Create(scriptCode, options, globalsType: context.GetType());
                            await compiledScript.RunAsync(globals: context);

                            await context.Client.SaveWorkItem(context.Self);
                            succeeded = true;
                        }
                        catch (CompilationErrorException ex)
                        {
                            succeeded = false;
                            var err = $"Script compilation error in file '{scriptFile}': {ex.Message}";
                            if (attempts < MAX_ATTEMPTS)
                                _logger.LogWarning($"Attempt {attempts} failed with an error: {err}, will retry");
                            else
                            {
                                _logger.LogError(err);
                                errCol.GetOrAdd(scriptFile, err);
                            }
                        }
                        catch (Exception ex)
                        {
                            succeeded = false;
                            var err = $"Error executing script '{scriptFile}': {ex.Message}";
                            if (attempts < MAX_ATTEMPTS)
                                _logger.LogWarning($"Attempt {attempts} failed with an error: {err}, will retry");
                            else
                            {
                                _logger.LogError(err);
                                errCol.GetOrAdd(scriptFile, err);
                            }
                        }
                    }

                });

                _logger.Log(LogLevel.Information, $"Done Executing all scripts");
            }
            catch (Exception ex)
            {
                var erro = $"Error executing scripts: {ex.Message}";
                _logger.LogError(erro);
                errCol.GetOrAdd("Error", erro);
            }

            if (errCol.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                foreach (var keyval in errCol)
                {
                    sb.AppendLine($"location: {keyval.Key}, error: {keyval.Value}");
                }
                err = sb.ToString();
            }
            return err;
        }

    }
}