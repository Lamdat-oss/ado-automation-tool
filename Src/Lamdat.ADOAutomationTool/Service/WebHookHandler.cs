using Lamdat.ADOAutomationTool.Entities;
using Lamdat.ADOAutomationTool.ScriptEngine;
using Newtonsoft.Json;

namespace Lamdat.ADOAutomationTool.Service
{
    public class WebHookHandler
    {
        private readonly ILogger _logger;
        private readonly Settings _settingsAccessor;
        private static ADOUser SystemUser { get; set; }

        public WebHookHandler(ILogger logger, Settings settingsAccessor)
        {
            _logger = logger;
            _settingsAccessor = settingsAccessor;
        }

        public async Task Init()
        {
            try
            {
                var adoClient = new AzureDevOpsClient(_logger, _settingsAccessor.CollectionURL, string.Empty, _settingsAccessor.PAT, _settingsAccessor.BypassRules);
                WebHookHandler.SystemUser = await adoClient.WhoAmI();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");

            }
        }
        /// <summary>
        /// Handles Payload
        /// </summary>
        /// <param name="payload"></param>
        /// <returns></returns>
        public async Task<string> HandleWebHook(string webHookBody)
        {
            string err = null;
            try
            {
                WebHookInfo? payload = JsonConvert.DeserializeObject<WebHookInfo>(webHookBody);

                var adoClient = new AzureDevOpsClient(_logger, _settingsAccessor.CollectionURL, payload.Project, _settingsAccessor.PAT, _settingsAccessor.BypassRules);
                if (payload.EventType == "workitem.created")
                    payload.Resource.WorkItemId = payload.Resource.Id;

                var witRcv = await adoClient.GetWorkItem(payload.Resource.WorkItemId);
                var context = new Context(webHookResource: payload.Resource, workitem: witRcv, project: payload.Project, eventType: payload.EventType, logger: _logger, client: adoClient);

                if (payload.EventType == "workitem.updated")
                {
                    var systemUserID = SystemUser.Identity.TeamFoundationId;
                    dynamic? userChanged = null;
                    var userChangedSuccess = witRcv.Fields.TryGetValue("System.ChangedBy", out userChanged);
                    if (userChangedSuccess == false)                    
                        _logger.LogWarning("Workitem changed user not found, will not run sripts");                    
                    else
                    {
                        var changedUsedId = userChanged.id;
                        if (changedUsedId == systemUserID)
                        {
                            _logger.LogDebug("Will not execute script since changed by ado automation system user");
                        }
                        else
                        {
                            var engine = new CSharpScriptEngine(_logger);
                            err = await engine.ExecuteScripts(context);
                        }
                    }
                }
                else {
                    var engine = new CSharpScriptEngine(_logger);
                    err = await engine.ExecuteScripts(context);
                }
              
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"WebHook handler failed: {ex.Message}");
                throw;
            }
            return err;
        }
    }
}
