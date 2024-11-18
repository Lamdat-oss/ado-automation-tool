using Lamdat.ADOAutomationTool.Entities;
using Lamdat.ADOAutomationTool.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using csscript;
using CSScriptLib;

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

            CSScript.EvaluatorConfig.Engine = EvaluatorEngine.Roslyn;

        }

        public async Task<string> ExecuteScripts(IContext context)
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
                var entityID = context.Self.Id;
                context.Self = await context.Client.GetWorkItem(context.Self.Id);
                if (context.Self == null || context.Self.Id == 0)
                {
                    _logger.Warning($"Entity with id {entityID} was not found, it may have been deleted");
                    return null;
                }                                      
                

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
                            StringBuilder stringBuilder = new StringBuilder();
                            stringBuilder.AppendLine(@"
using Lamdat.ADOAutomationTool.Entities;
using Lamdat.ADOAutomationTool.Service;
using Serilog;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;

    public async Task Run(IAzureDevOpsClient Client, string EventType, ILogger Logger, string? Project, Relations RelationChanges, WorkItem Self, Dictionary<string, object> SelfChanges, WebHookResourceUpdate WebHookResource)
    {");
                            stringBuilder.AppendLine(scriptCode);
                            stringBuilder.AppendLine("}");                           
                            scriptCode = stringBuilder.ToString();

                          
                            lock (_lock)
                            {
                                var script = CSScript.Evaluator.LoadMethod<IScript>(scriptCode);
                                //var script = CSharpScript.Create(scriptCode, options, globalsType: context.GetType());
                                //var runner = script.CreateDelegate();
                                try
                                {
                                    script.Run(context.Client, context.EventType, context.Logger, context.Project, context.RelationChanges, context.Self, context.SelfChanges, context.WebHookResource).Wait();

                                    //var result = runner(context).Result;
                                    //compiledScript.RunAsync(globals: context).Wait();

                                    context.Client.SaveWorkItem(context.Self, attempts == MAX_ATTEMPTS).Wait();
                                }
                                finally
                                {
                                    script = null;
                                }
                            }

                            succeeded = true;
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

        private void LogExecutionAttempt(IContext context, string scriptFile, int attempts)
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

public interface IScript
{
    Task Run(IAzureDevOpsClient Client, string EventType, Serilog.ILogger Logger, string? Project, Relations RelationChanges, WorkItem Self, Dictionary<string, object> SelfChanges, WebHookResourceUpdate WebHookResource);
}

