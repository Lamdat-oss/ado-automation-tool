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
                var adoClient = new AzureDevOpsClient(_logger, _settingsAccessor.CollectionURL, string.Empty, _settingsAccessor.PAT, _settingsAccessor.BypassRules, _settingsAccessor.NotValidCertificates);
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

                var adoClient = new AzureDevOpsClient(_logger, _settingsAccessor.CollectionURL, payload.Project, _settingsAccessor.PAT, _settingsAccessor.BypassRules, _settingsAccessor.NotValidCertificates);
                if (payload.EventType == "workitem.created")
                    payload.Resource.WorkItemId = payload.Resource.Id;

                ADOUser? lastRevisionUser = await adoClient.GetLastChangedByUserForWorkItem(payload.Resource.WorkItemId);
                WorkItem? witRcv = await adoClient.GetWorkItem(payload.Resource.WorkItemId);
                Dictionary<string, object> selfChangedDic;
                if (payload.EventType == "workitem.updated")
                    selfChangedDic = payload.Resource.Fields as Dictionary<string, object>;
                else
                    selfChangedDic = new Dictionary<string, object>();

                var context = new Context(webHookResource: payload.Resource, selfChanges: selfChangedDic, relationChanges: payload.Resource.Relations, workitem: witRcv, project: payload.Project, eventType: payload.EventType, logger: _logger, client: adoClient);

                if (payload.EventType == "workitem.updated")
                {
                    var systemUserUniqueName = SystemUser.Identity.SubHeader;
                    //dynamic? userChanged = null;
                    //var userChangedSuccess = witRcv.Fields.TryGetValue("System.ChangedBy", out userChanged);
                    //if (lastRevisionUser == null)
                    //    _logger.LogDebug("No Revisions found");
                    //else
                    //{
                    var nonTriggerFields = new List<string>() { "System.Rev", "System.AuthorizedDate", "System.RevisedDate", "System.ChangedDate", "System.Watermark" };
                    var selfChangedDicCheck = new Dictionary<string, object>();
                    foreach (var item in selfChangedDic)
                    {
                        if (!nonTriggerFields.Contains(item.Key))
                            selfChangedDicCheck.Add(item.Key, item.Value);
                    }
                    var changedUsedUniqueName = lastRevisionUser.Identity.SubHeader;
                    if (changedUsedUniqueName == systemUserUniqueName && selfChangedDic.Count > 0 && selfChangedDicCheck.Count == 0) //stop condition
                    {
                        _logger.LogDebug("Will not execute script since changed by ado automation system user");
                    }
                    else if (selfChangedDic.Count > 0 && selfChangedDicCheck.Count == 0)
                    {
                        _logger.LogDebug("Will not execute script since irrelevant fields were updated");

                    }
                    else
                    {
                        var engine = new CSharpScriptEngine(_logger);
                        err = await engine.ExecuteScripts(context);
                    }
                    //}
                }
                else
                {
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
