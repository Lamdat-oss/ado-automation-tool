using Serilog;

namespace Lamdat.ADOAutomationTool.Entities
{
    public interface IContext
    {
        IAzureDevOpsClient Client { get; set; }
        string EventType { get; set; }
        ILogger Logger { get; set; }
        string? Project { get; set; }
        Relations RelationChanges { get; set; }
        WorkItem Self { get; set; }
        Dictionary<string, object> SelfChanges { get; set; }
        WebHookResourceUpdate WebHookResource { get; set; }
        void SetProject(string projectName);
        public int ScriptExecutionTimeoutSeconds { get; set; }
        public string ScriptRunId { get; set; }

    }
}