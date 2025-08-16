using Serilog.Core;
using Serilog.Events;

namespace Lamdat.ADOAutomationTool.Tests.Framework
{
    /// <summary>
    /// Test log sink to capture log messages for testing
    /// </summary>
    internal class TestLogSink : ILogEventSink
    {
        private readonly List<string> _logMessages;

        public TestLogSink(List<string> logMessages)
        {
            _logMessages = logMessages;
        }

        public void Emit(LogEvent logEvent)
        {
            var message = logEvent.RenderMessage();
            _logMessages.Add($"[{logEvent.Level}] {message}");
        }
    }
}