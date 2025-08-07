namespace Lamdat.ADOAutomationTool.Tests.Framework
{
    /// <summary>
    /// Result of executing a scheduled script test
    /// </summary>
    public class ScheduledScriptTestResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public Exception? Exception { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan ExecutionTime { get; set; }
        public List<string> LogMessages { get; set; } = new();

        public bool HasLogMessage(string message)
        {
            return LogMessages.Any(log => log.Contains(message));
        }

        public bool HasLogMessageContaining(string substring)
        {
            return LogMessages.Any(log => log.Contains(substring, StringComparison.OrdinalIgnoreCase));
        }

        public List<string> GetLogMessagesContaining(string substring)
        {
            return LogMessages.Where(log => log.Contains(substring, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }
}