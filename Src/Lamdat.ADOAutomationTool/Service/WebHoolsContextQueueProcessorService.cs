using System;
using System.Threading;
using System.Threading.Tasks;
using Lamdat.ADOAutomationTool.Entities;
using Lamdat.ADOAutomationTool.ScriptEngine;
using Serilog;

namespace Lamdat.ADOAutomationTool.Service
{
    /// <summary>
    /// Service responsible for processing queued <see cref="IContext"/> items by executing scripts using <see cref="CSharpScriptEngine"/>.
    /// </summary>
    /// <remarks>
    /// This service uses a timer to periodically check the <see cref="WebHookContextQueue"/> for new items and processes them asynchronously.
    /// </remarks>
    public class WebHoolsContextQueueProcessorService : IDisposable
    {
        /// <summary>
        /// The queue holding <see cref="IContext"/> items to be processed.
        /// </summary>
        private readonly WebHookContextQueue _contextQueue;

        /// <summary>
        /// The script engine used to execute scripts for each context.
        /// </summary>
        private readonly CSharpScriptEngine _scriptEngine;

        /// <summary>
        /// Logger for error and information messages.
        /// </summary>
        private readonly Serilog.ILogger _logger;

        /// <summary>
        /// Timer that triggers the queue processing at regular intervals.
        /// </summary>
        private readonly Timer _timer;

        /// <summary>
        /// Indicates whether the queue is currently being processed.
        /// </summary>
        private bool _isProcessing;

        /// <summary>
        /// Indicates whether the service has been disposed.
        /// </summary>
        private bool _disposed;

        public WebHoolsContextQueueProcessorService(WebHookContextQueue contextQueue, CSharpScriptEngine scriptEngine, Serilog.ILogger logger)
        {
            _contextQueue = contextQueue;
            _scriptEngine = scriptEngine;
            _logger = logger;
            _timer = new Timer(ProcessQueue, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        }

        private DateTime _lastQueueLogTime = DateTime.MinValue;

        private async void ProcessQueue(object? state)
        {
            if (_isProcessing) return;
            _isProcessing = true;
            try
            {
                while (_contextQueue.TryDequeue(out var context))
                {
                    try
                    {
                        var err = await _scriptEngine.ExecuteScripts(context);
                        if (err != null)
                        {
                            _logger.Error($"Error executing scripts for context with workitem Type '{context.Self?.WorkItemType}', Id '{context.Self?.Id}', Title '{context.Self?.Title}': {err}");
                        }
                        else
                        {
                            _logger.Debug($"Successfully executed scripts for context {context.Self.Id}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Error executing scripts for context: {ex.Message}");
                    }

                    // Log queue count every 1 minute if there are items pending
                    if (_contextQueue.Count > 0 && (DateTime.UtcNow - _lastQueueLogTime).TotalMinutes >= 1)
                    {
                        _logger.Information($"*************** There are {_contextQueue.Count} items pending in the queue for processing. ***********");
                        _lastQueueLogTime = DateTime.UtcNow;
                    }
                }
            }
            finally
            {
                _isProcessing = false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _timer.Dispose();
            _disposed = true;
        }
    }
}

