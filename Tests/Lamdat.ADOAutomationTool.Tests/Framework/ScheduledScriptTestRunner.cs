using Lamdat.ADOAutomationTool.Entities;
using Lamdat.ADOAutomationTool.ScriptEngine;
using CSScriptLib;
using Serilog;
using System.Text;
using csscript;

namespace Lamdat.ADOAutomationTool.Tests.Framework
{
    /// <summary>
    /// Test framework for executing and testing scheduled scripts
    /// </summary>
    public class ScheduledScriptTestRunner : IDisposable
    {
        private readonly MockAzureDevOpsClient _mockClient;
        private readonly ILogger _logger;
        private readonly object _lock = new object();
        private readonly List<string> _logMessages = new();
        private bool _disposed = false;

        public ScheduledScriptTestRunner()
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
        /// Execute a scheduled script from a string
        /// </summary>
        public async Task<ScheduledScriptTestResult> ExecuteScriptAsync(string scriptCode, CancellationToken cancellationToken = default)
        {
            var result = new ScheduledScriptTestResult();
            result.StartTime = DateTime.UtcNow;

            try
            {
                // Clear previous log messages
                _logMessages.Clear();

                // Build the script wrapper
                var wrappedScript = WrapScript(scriptCode);

                IScheduledScript script;
                lock (_lock)
                {
                    script = CSScript.Evaluator.LoadMethod<IScheduledScript>(wrappedScript);
                }

                // Execute the script
                await script.Run(_mockClient, _logger, cancellationToken, Guid.NewGuid().ToString());

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
        /// Execute a scheduled script from a file
        /// </summary>
        public async Task<ScheduledScriptTestResult> ExecuteScriptFromFileAsync(string scriptFilePath, CancellationToken cancellationToken = default)
        {
            var scriptCode = await File.ReadAllTextAsync(scriptFilePath, cancellationToken);
            return await ExecuteScriptAsync(scriptCode, cancellationToken);
        }

        /// <summary>
        /// Create a test work item and return its ID
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

public async Task Run(IAzureDevOpsClient Client, ILogger Logger, CancellationToken cancellationToken, string ScriptRunId)
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
                //_logger?.Dispose();
                _disposed = true;
            }
        }
    }
}