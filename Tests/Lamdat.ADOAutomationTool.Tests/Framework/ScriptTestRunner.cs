using Lamdat.ADOAutomationTool.Entities;
using Lamdat.ADOAutomationTool.ScriptEngine;
using CSScriptLib;
using Serilog;
using System.Text;
using csscript;

namespace Lamdat.ADOAutomationTool.Tests.Framework
{
    /// <summary>
    /// Test framework for executing and testing webhook/context-based scripts (IScript)
    /// </summary>
    public class ScriptTestRunner : IDisposable
    {
        private readonly MockAzureDevOpsClient _mockClient;
        private readonly ILogger _logger;
        private readonly object _lock = new object();
        private readonly List<string> _logMessages = new();
        private bool _disposed = false;

        public ScriptTestRunner()
        {
            _mockClient = new MockAzureDevOpsClient();
            
            // Create a logger that captures messages for testing
            _logger = new LoggerConfiguration()
                .WriteTo.Sink(new TestLogSink(_logMessages))
                .CreateLogger();

            // Configure CS-Script
            CSScript.EvaluatorConfig.Engine = EvaluatorEngine.Roslyn;
        }

        public MockAzureDevOpsClient MockClient => _mockClient;
        public IReadOnlyList<string> LogMessages => _logMessages.AsReadOnly();

        /// <summary>
        /// Execute a webhook script with a given context
        /// </summary>
        public async Task<ScriptTestResult> ExecuteScriptAsync(string scriptCode, IContext context, CancellationToken cancellationToken = default)
        {
            var result = new ScriptTestResult();
            result.StartTime = DateTime.UtcNow;

            try
            {
                // Clear previous log messages
                _logMessages.Clear();

                // Build the script wrapper
                var wrappedScript = WrapScript(scriptCode);

                IScript script;
                lock (_lock)
                {
                    script = CSScript.Evaluator.LoadMethod<IScript>(wrappedScript);
                }

                // Execute the script
                await script.Run(
                    context.Client,
                    context.EventType,
                    context.Logger,
                    context.Project,
                    context.RelationChanges,
                    context.Self,
                    context.SelfChanges,
                    context.WebHookResource,
                    cancellationToken,
                    context.ScriptRunId
                );

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Exception = ex;
                result.ErrorMessage = ex.Message;
            }
            finally
            {
                result.EndTime = DateTime.UtcNow;
                result.ExecutionTime = result.EndTime - result.StartTime;
                result.LogMessages = _logMessages.ToList();
            }

            return result;
        }

        /// <summary>
        /// Execute a script with simplified parameters
        /// </summary>
        public async Task<ScriptTestResult> ExecuteScriptAsync(
            string scriptCode, 
            WorkItem workItem = null,
            string eventType = "workitem.updated",
            string project = null,
            CancellationToken cancellationToken = default)
        {
            var context = CreateTestContext(workItem, eventType, project);
            return await ExecuteScriptAsync(scriptCode, context, cancellationToken);
        }

        /// <summary>
        /// Execute a script from a file
        /// </summary>
        public async Task<ScriptTestResult> ExecuteScriptFromFileAsync(string scriptFilePath, IContext context, CancellationToken cancellationToken = default)
        {
            var scriptCode = await File.ReadAllTextAsync(scriptFilePath, cancellationToken);
            return await ExecuteScriptAsync(scriptCode, context, cancellationToken);
        }

        /// <summary>
        /// Execute a script from a file with simplified parameters
        /// </summary>
        public async Task<ScriptTestResult> ExecuteScriptFromFileAsync(
            string scriptFilePath,
            WorkItem workItem = null,
            string eventType = "workitem.updated",
            string project = null,
            CancellationToken cancellationToken = default)
        {
            var context = CreateTestContext(workItem, eventType, project);
            return await ExecuteScriptFromFileAsync(scriptFilePath, context, cancellationToken);
        }

        /// <summary>
        /// Create a test work item
        /// </summary>
        public WorkItem CreateTestWorkItem(string workItemType = "Task", string title = "Test Work Item", string state = "New")
        {
            return _mockClient.CreateTestWorkItem(workItemType, title, state);
        }

        /// <summary>
        /// Add test iteration data
        /// </summary>
        public void AddTestIteration(string teamName, string iterationName, DateTime startDate, DateTime endDate)
        {
            _mockClient.AddIteration(teamName, iterationName, startDate, endDate);
        }

        /// <summary>
        /// Clear all test data
        /// </summary>
        public void ClearTestData()
        {
            _mockClient.ClearAllData();
            _logMessages.Clear();
        }

        /// <summary>
        /// Create a test context for script execution
        /// </summary>
        public IContext CreateTestContext(WorkItem workItem = null, string eventType = "workitem.updated", string project = null)
        {
            workItem ??= CreateTestWorkItem();
            project ??= _mockClient.Project;

            var context = new Context(_mockClient, _logger)
            {
                Self = workItem,
                EventType = eventType,
                Project = project,
                ScriptRunId = Guid.NewGuid().ToString(),
                SelfChanges = new Dictionary<string, object>(),
                RelationChanges = new Relations(),
                WebHookResource = new WebHookResourceUpdate
                {
                    Id = workItem.Id,
                    WorkItemId = workItem.Id,
                    Fields = workItem.Fields?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value) ?? new Dictionary<string, object>()
                }
            };

            return context;
        }

        /// <summary>
        /// Create a context that simulates a field change event
        /// </summary>
        public IContext CreateFieldChangeContext(WorkItem workItem, string fieldName, object oldValue, object newValue)
        {
            var context = CreateTestContext(workItem, "workitem.updated");
            context.SelfChanges[fieldName] = new { oldValue, newValue };
            return context;
        }

        /// <summary>
        /// Create a context that simulates a state change event
        /// </summary>
        public IContext CreateStateChangeContext(WorkItem workItem, string oldState, string newState)
        {
            return CreateFieldChangeContext(workItem, "System.State", oldState, newState);
        }

        /// <summary>
        /// Create a context that simulates a work item creation event
        /// </summary>
        public IContext CreateWorkItemCreatedContext(WorkItem workItem)
        {
            return CreateTestContext(workItem, "workitem.created");
        }

        private string WrapScript(string scriptCode)
        {
            var stringBuilder = new StringBuilder();
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

public async Task Run(IAzureDevOpsClient Client, string EventType, ILogger Logger, string? Project, Relations RelationChanges, WorkItem Self, Dictionary<string, object> SelfChanges, WebHookResourceUpdate WebHookResource, CancellationToken cancellationToken, string ScriptRunId)
{");
            stringBuilder.AppendLine(scriptCode);
            stringBuilder.AppendLine("}");
            
            return stringBuilder.ToString();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _disposed = true;
            }
        }
    }
}