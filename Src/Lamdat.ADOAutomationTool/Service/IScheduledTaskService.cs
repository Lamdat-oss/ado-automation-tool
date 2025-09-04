using System.Collections.Generic;
using System.Threading.Tasks;
using Lamdat.ADOAutomationTool.Entities;

namespace Lamdat.ADOAutomationTool.Service
{
    public interface IScheduledTaskService
    {
        Task Start();
        void Stop();
        
        /// <summary>
        /// Gets information about all scheduled scripts and their next execution times
        /// </summary>
        Dictionary<string, ScheduledScriptInfo> GetScheduleInfo();
    }
}