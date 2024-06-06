using Lamdat.ADOAutomationTool.Service;

namespace Lamdat.ADOAutomationTool.Entities
{
    public class Context
    {
        public Context(WebHookResource webHookResource, Dictionary<string, object> selfChanges, Relations relationChanges, WorkItem workitem, string project, string eventType, ILogger logger, AzureDevOpsClient client)
        {
            WebHookResource = webHookResource;
            SelfChanges = selfChanges;
            Project = project;
            Self = workitem;
            EventType = eventType;
            Logger = logger;
            Client = client;
            RelationChanges = relationChanges;
        }

        public string? Project { get; set; }

        public WebHookResource WebHookResource { get; set; }

        public WorkItem Self { get; set; }

        public string EventType { get; private set; }

        public ILogger Logger { get; private set; }

        public AzureDevOpsClient Client { get; private set; }

        public Dictionary<string, object> SelfChanges { get; set; }

        public Relations RelationChanges { get; set; }
    }
}
