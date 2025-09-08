using Lamdat.ADOAutomationTool.Entities;
using Lamdat.ADOAutomationTool.ScriptEngine;
using CSScriptLib;
using Serilog;
using System.Text;


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

                // Try to execute as interval-aware script first
                var intervalResult = await TryExecuteAsIntervalScript(scriptCode, cancellationToken);
                if (intervalResult != null)
                {
                    // Script executed successfully (no exception), store the result
                    result.Success = true; // Script execution was successful
                    result.ScheduledScriptResult = intervalResult;
                    result.NextExecutionIntervalMinutes = intervalResult.NextExecutionIntervalMinutes;
                    
                    // Note: intervalResult.IsSuccess indicates whether the script's business logic succeeded,
                    // but result.Success indicates whether the script executed without throwing an exception
                }
                else
                {
                    // Fallback to standard scheduled script execution
                    await ExecuteAsStandardScript(scriptCode, cancellationToken);
                    result.Success = true;
                }
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
        /// Try to execute script as an interval-aware script
        /// </summary>
        private async Task<Lamdat.ADOAutomationTool.Entities.ScheduledScriptResult?> TryExecuteAsIntervalScript(string scriptCode, CancellationToken cancellationToken)
        {
            try
            {
                var wrappedScript = WrapScriptAsInterval(scriptCode);

                IScheduledScriptWithInterval script;
                lock (_lock)
                {
                    script = CSScript.Evaluator.LoadMethod<IScheduledScriptWithInterval>(wrappedScript);
                }

                // Use a default LastRun for testing (1 hour ago)
                var testLastRun = DateTime.Now.AddHours(-1);
                return await script.RunWithInterval(_mockClient, _logger, cancellationToken, Guid.NewGuid().ToString(), testLastRun);
            }
            catch
            {
                // Script doesn't implement interval interface, return null to indicate fallback needed
                return null;
            }
        }

        /// <summary>
        /// Execute script as standard scheduled script
        /// </summary>
        private async Task ExecuteAsStandardScript(string scriptCode, CancellationToken cancellationToken)
        {
            var wrappedScript = WrapScriptAsStandard(scriptCode);

            IScheduledScript script;
            lock (_lock)
            {
                script = CSScript.Evaluator.LoadMethod<IScheduledScript>(wrappedScript);
            }

            // Use a default LastRun for testing (1 hour ago)
            var testLastRun = DateTime.Now.AddHours(-1);
            await script.Run(_mockClient, _logger, cancellationToken, Guid.NewGuid().ToString(), testLastRun);
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

        private string WrapScriptAsInterval(string scriptCode)
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine(@"
using Lamdat.ADOAutomationTool.Entities;
using Lamdat.ADOAutomationTool.Service;
using Serilog;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System;

public async Task<ScheduledScriptResult> RunWithInterval(IAzureDevOpsClient Client, ILogger Logger, CancellationToken CancellationToken, string ScriptRunId, DateTime LastRun)
{
    var Token = CancellationToken;");
            stringBuilder.AppendLine(scriptCode);
            stringBuilder.AppendLine("}");
            
            return stringBuilder.ToString();
        }

        private string WrapScriptAsStandard(string scriptCode)
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine(@"
using Lamdat.ADOAutomationTool.Entities;
using Lamdat.ADOAutomationTool.Service;
using Serilog;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System;

public async Task Run(IAzureDevOpsClient Client, ILogger Logger, CancellationToken CancellationToken, string ScriptRunId, DateTime LastRun)
{
    var Token = CancellationToken;");
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
                // ILogger doesn't need disposal in our test setup
                _disposed = true;
            }
        }
    }
}