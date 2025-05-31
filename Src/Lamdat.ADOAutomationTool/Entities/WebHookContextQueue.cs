using System.Collections.Concurrent;

namespace Lamdat.ADOAutomationTool.Entities
{
    public class WebHookContextQueue
    {
        private static readonly ConcurrentQueue<IContext> _contexts = new();

        private readonly Serilog.ILogger _logger;
        private readonly Settings _settings;

        public WebHookContextQueue(Serilog.ILogger logger, Settings settings)
        {
            _logger = logger;
            _settings = settings ?? throw new ArgumentNullException(nameof(settings), "Settings cannot be null");
        }

        public string Enqueue(IContext context)
        {
            if (_contexts.Count > _settings.MaxQueueWebHookRequestCount)
            {
                _logger.Error($"WebHookContextQueue size exceeded the maximum limit of {_settings.MaxQueueWebHookRequestCount}. Consider increasing the limit or processing items more frequently.");
                return $"WebHookContextQueue size exceeded the maximum limit of {_settings.MaxQueueWebHookRequestCount}. Consider increasing the limit or processing items more frequently.";
            }
            _contexts.Enqueue(context);
            return null;


        }

        public bool TryDequeue(out IContext context)
        {
            return _contexts.TryDequeue(out context);
        }

        public int Count => _contexts.Count;

        public void Clear()
        {
            while (_contexts.TryDequeue(out _)) { }
        }
    }
}