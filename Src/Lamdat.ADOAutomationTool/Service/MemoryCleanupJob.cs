namespace Lamdat.ADOAutomationTool.Service
{
    using System;
    using System.Timers;

    public class MemoryCleaner : IMemoryCleaner
    {
        private Timer _timer;
        private readonly double _interval;
        private readonly Serilog.ILogger _logger;

        public MemoryCleaner(Serilog.ILogger logger, double intervalInMinutes)
        {
            _interval = intervalInMinutes * 60 * 1000;
            _timer = new Timer(_interval);
            _timer.Elapsed += OnTimedEvent;
            _timer.AutoReset = true;
            _logger = logger;
        }

        public void Activate()
        {
            if (_interval == 0)
            {
                _logger.Information($"Memory Cleaner Will not run since it has an interval of 0 minutes.");
            }
            else
            {
                _timer.Start();
                _logger.Information($"Memory Cleaner Activated, will run every {_interval} minutes.");
            }
        }

        public void Deactivate()
        {
            _timer.Stop();
            _logger.Information("Memory Cleaner Deactivated.");
        }

        private void OnTimedEvent(Object source, ElapsedEventArgs e)
        {
            _logger.Debug($"GC Collect triggered at {DateTime.Now}");
            GC.Collect();
        }

    }


}
