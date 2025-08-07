namespace Lamdat.ADOAutomationTool.Service
{
    public interface IScheduledTaskService
    {
        void Start();
        void Stop();
        bool IsRunning { get; }
    }
}