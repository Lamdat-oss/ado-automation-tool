using Serilog;

namespace Lamdat.ADOAutomationTool.Entities
{
    public interface IScheduledContext
    {
        IAzureDevOpsClient Client { get; set; }      
        void SetProject(string projectName);
        public int ScriptExecutionTimeoutSeconds { get; set; }
        public string ScriptRunId { get; set; }
        public string? Project { get; set; }

        public ILogger Logger { get; set; }

       

    }
}