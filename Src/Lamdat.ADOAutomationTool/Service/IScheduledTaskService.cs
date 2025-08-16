using System.Collections.Generic;
using Lamdat.ADOAutomationTool.ScriptEngine;

namespace Lamdat.ADOAutomationTool.Service
{
    public interface IScheduledTaskService
    {
        void Start();
        void Stop();
        bool IsRunning { get; }
        
        /// <summary>
        /// Gets information about all scheduled scripts and their next execution times
        /// </summary>
        Dictionary<string, ScheduledScriptInfo> GetScheduleInfo();
    }
}