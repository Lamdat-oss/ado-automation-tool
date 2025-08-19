using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lamdat.ADOAutomationTool.Entities
{
    public interface IScheduledScript
    {
        Task Run(
            IAzureDevOpsClient Client,
            ILogger Logger,       
            CancellationToken CancellationToken,
            string ScriptRunId,
            DateTime LastRun);
    }

    /// <summary>
    /// Interface for scheduled scripts that can define their own execution interval
    /// </summary>
    public interface IScheduledScriptWithInterval
    {
        Task<ScheduledScriptResult> RunWithInterval(
            IAzureDevOpsClient Client,
            ILogger Logger,       
            CancellationToken CancellationToken,
            string ScriptRunId,
            DateTime LastRun);
    }

    /// <summary>
    /// Result returned by scheduled scripts that includes execution interval information
    /// </summary>
    public class ScheduledScriptResult
    {
        /// <summary>
        /// Interval in minutes for the next execution of this script. 
        /// If null or 0, the script will use the global default interval.
        /// </summary>
        public int? NextExecutionIntervalMinutes { get; set; }

        /// <summary>
        /// Indicates whether the script executed successfully
        /// </summary>
        public bool IsSuccess { get; set; } = true;

        /// <summary>
        /// Optional message from the script execution
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// Creates a successful result with specified interval
        /// </summary>
        public static ScheduledScriptResult Success(int? intervalMinutes = null, string? message = null)
        {
            return new ScheduledScriptResult
            {
                IsSuccess = true,
                NextExecutionIntervalMinutes = intervalMinutes,
                Message = message
            };
        }

        /// <summary>
        /// Creates a failed result
        /// </summary>
        public static ScheduledScriptResult Failure(string? message = null)
        {
            return new ScheduledScriptResult
            {
                IsSuccess = false,
                Message = message
            };
        }
    }

    /// <summary>
    /// Information about a scheduled script's execution timing
    /// </summary>
    public class ScheduledScriptInfo
    {
        public string ScriptPath { get; set; } = string.Empty;
        public DateTime LastExecuted { get; set; }
        public DateTime? PreviousLastExecuted { get; set; }
        public int IntervalMinutes { get; set; }
        public DateTime NextScheduledExecution => LastExecuted.AddMinutes(IntervalMinutes);
        public bool ShouldExecuteNow => DateTime.Now >= NextScheduledExecution;
        public bool IsFirstRun { get; set; } = true;
    }
}