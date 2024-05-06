using Lamdat.ADOAutomationTool.Service;

namespace Lamdat.ADOAutomationTool.Entities
{
    public class Context
    {
        public Context(WebHookResource webHookResource, WorkItem workitem, string project, string eventType, ILogger logger, AzureDevOpsClient client)
        {
            WebHookResource = webHookResource;
            Project = project;
            Self = workitem;
            EventType = eventType;
            Logger = logger;
            Client = client;
        }

        public string? Project { get; set; }

        public WebHookResource WebHookResource { get; set; }

        public WorkItem Self { get; private set; }

        public string EventType { get; private set; }

        public ILogger Logger { get; private set; }

        public AzureDevOpsClient Client { get; private set; }
    }
}
