
using Serilog;

namespace Lamdat.ADOAutomationTool.Entities
{
    public class Context : IContext
    {
        public Context(IAzureDevOpsClient azureDevOpsClient, ILogger logger)
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

        public WebHookResourceUpdate WebHookResource { get; set; }

        public WorkItem Self { get; set; }

        public string EventType { get; set; }

        public ILogger Logger { get; set; }

        public IAzureDevOpsClient Client { get; set; }

        public Dictionary<string, object> SelfChanges { get; set; }

        public Relations RelationChanges { get; set; }

        public int ScriptExecutionTimeoutSeconds { get; set; } = 60; 
    }
}
