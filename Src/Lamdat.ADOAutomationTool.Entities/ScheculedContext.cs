
using Serilog;

namespace Lamdat.ADOAutomationTool.Entities
{
    public class ScheculedContext : IScheduledContext
    {
        public ScheculedContext(IAzureDevOpsClient azureDevOpsClient, ILogger logger)
        {
            Logger = logger;
            azureDevOpsClient.Project = this.Project;
            Client = azureDevOpsClient;
        }

        public void SetProject(string projectName)
        {
            Client.Project = projectName;
        }

        public string? Project { get; set; }
 
        public ILogger Logger { get; set; }

        public IAzureDevOpsClient Client { get; set; }

        public int ScriptExecutionTimeoutSeconds { get; set; } = 60; 
        
        public string ScriptRunId { get; set; } 
    }
}
