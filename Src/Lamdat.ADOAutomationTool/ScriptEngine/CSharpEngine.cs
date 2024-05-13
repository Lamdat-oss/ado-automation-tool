using Lamdat.ADOAutomationTool.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text;


namespace Lamdat.ADOAutomationTool.ScriptEngine
{
    public class CSharpScriptEngine
    {
        private readonly ILogger _logger;
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
                    MaxDegreeOfParallelism = Environment.ProcessorCount * 2
                };

                Parallel.ForEach(scriptFiles, parallelOptions, async scriptFile =>
                {
                    try
                    {
                        _logger.Log(LogLevel.Information, $"Event '{context.EventType}', executing script {scriptFile}");

                        string scriptCode = await File.ReadAllTextAsync(scriptFile);
                        ScriptOptions options = ScriptOptions.Default
                            .AddReferences(typeof(WebHookInfo).Assembly)
                            .AddImports("Lamdat.ADOAutomationTool.Entities")
                            .AddImports("Microsoft.Extensions.Logging")
                            .AddImports("System");
                        await CSharpScript.EvaluateAsync(scriptCode, options, globals: context);
                        var compiledScript = CSharpScript.Create(scriptCode, options, globalsType: context.GetType());
                        await compiledScript.RunAsync(globals: context);

                        await context.Client.SaveWorkItem(context.Self);
                    }
                    catch (CompilationErrorException ex)
                    {
                        var err = $"Script compilation error in file '{scriptFile}': {ex.Message}";
                        _logger.LogError(err);
                        errCol.GetOrAdd(scriptFile, err);
                    }
                    catch (Exception ex)
                    {
                        var err = $"Error executing script '{scriptFile}': {ex.Message}";
                        _logger.LogError(err);
                        errCol.GetOrAdd(scriptFile, err);
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