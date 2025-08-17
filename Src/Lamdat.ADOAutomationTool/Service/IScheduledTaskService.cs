using System.Collections.Generic;
using System.Threading.Tasks;
using Lamdat.ADOAutomationTool.ScriptEngine;

namespace Lamdat.ADOAutomationTool.Service
{
    public interface IScheduledTaskService
    {
        Task Start();
        void Stop();
        bool IsRunning { get; }
        
        /// <summary>
        /// Gets information about all scheduled scripts and their next execution times
        /// </summary>
        Dictionary<string, ScheduledScriptInfo> GetScheduleInfo();
    }
}