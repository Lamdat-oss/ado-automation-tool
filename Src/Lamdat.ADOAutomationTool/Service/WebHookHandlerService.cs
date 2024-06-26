using Lamdat.ADOAutomationTool.Entities;
using Lamdat.ADOAutomationTool.ScriptEngine;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Lamdat.ADOAutomationTool.Service
{
    public class WebHookHandlerService : IWebHookHandlerService
    {
        private readonly Serilog.ILogger _logger;
        private readonly Settings _setting;
        private static ADOUser SystemUser { get; set; }
        private readonly CSharpScriptEngine _scriptEngine;
        private IContext _context;
        private IAzureDevOpsClient _client;

        public WebHookHandlerService(CSharpScriptEngine scriptEngine, Serilog.ILogger logger, IContext context, IOptions<Settings> settingsAccessor, IAzureDevOpsClient client)
        {
            _logger = logger;
            _scriptEngine = scriptEngine;
            _setting = settingsAccessor.Value;
            _context = context;
            _client = client;
        }

        public async Task Init()
        {
            try
            {
                WebHookHandlerService.SystemUser = await _client.WhoAmI();
            }
            catch (Exception ex)
            {
                _logger.Error($"WebHook handler failed with getting revisions: {ex.Message}");


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
                WebHookInfo<WebHookResourceBase>? payloadBase = JsonConvert.DeserializeObject<WebHookInfo<WebHookResourceBase>>(webHookBody);
                WebHookInfo<WebHookResourceUpdate> payloadmerged = new WebHookInfo<WebHookResourceUpdate>();
                payloadmerged.Project = payloadBase.Project;
                payloadmerged.ResourceContainers = payloadBase.ResourceContainers;
                payloadmerged.EventType = payloadBase.EventType;
                payloadmerged.ResourceContainers = payloadBase.ResourceContainers;
                payloadmerged.Resource = new WebHookResourceUpdate();
                payloadmerged.Resource.Id = payloadBase.Resource.Id;
                payloadmerged.Resource.WorkItemId = payloadBase.Resource.WorkItemId;
                payloadmerged.Resource.Revision = payloadBase.Resource.Revision;
                payloadmerged.Resource.Fields = payloadBase.Resource.Fields;


                //payloadBase.Project,
                if (payloadBase.EventType == "workitem.created")
                {
                    payloadBase.Resource.WorkItemId = payloadBase.Resource.Id;
                    payloadmerged.Resource.WorkItemId = payloadBase.Resource.Id;
                    WebHookInfo<WebHookResourceCreate>? payloadCreate = JsonConvert.DeserializeObject<WebHookInfo<WebHookResourceCreate>>(webHookBody);
                    payloadmerged.Resource.Relations = new Relations();
                    payloadmerged.Resource.Relations.Added = payloadCreate.Resource.Relations;

                }
                else if (payloadBase.EventType == "workitem.updated")
                {
                    WebHookInfo<WebHookResourceUpdate>? payloadUpdated = JsonConvert.DeserializeObject<WebHookInfo<WebHookResourceUpdate>>(webHookBody);
                    payloadmerged.Resource.Relations = payloadUpdated.Resource.Relations;
                }



                WorkItem? witRcv = await _client.GetWorkItem(payloadmerged.Resource.WorkItemId);
                ADOUser? lastRevisionUser = null;
                if (witRcv != null)
                    try
                    {
                        lastRevisionUser = await _client.GetLastChangedByUserForWorkItem(payloadmerged.Resource.WorkItemId);

                    }
                    catch (Exception ex) // can be an issue with test connection
                    {

                        _logger.Debug($"WebHook handler failed with getting revisions: {ex.Message}");

                    }


                Dictionary<string, object> selfChangedDic;
                if (payloadBase.EventType == "workitem.updated")
                    selfChangedDic = payloadmerged.Resource.Fields as Dictionary<string, object>;
                else
                    selfChangedDic = new Dictionary<string, object>();

                _context.Self = witRcv;
                _context.SelfChanges = selfChangedDic;
                _context.RelationChanges = payloadmerged.Resource.Relations;
                _context.Project = payloadmerged.Project;
                _context.SetProject(payloadmerged.Project);
                _context.EventType = payloadmerged.EventType;

                //var context = new Context(webHookResource: payloadmerged.Resource, selfChanges: selfChangedDic, relationChanges: payloadmerged.Resource.Relations, workitem: witRcv, project: payloadmerged.Project, eventType: payloadmerged.EventType);

                if (payloadBase.EventType == "workitem.updated")
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
                    string changedUsedUniqueName = lastRevisionUser == null ? null : lastRevisionUser.Identity.SubHeader;
                    if (changedUsedUniqueName == systemUserUniqueName && selfChangedDic.Count > 0 && selfChangedDicCheck.Count == 0) //stop condition
                    {
                        _logger.Debug("Will not execute script since changed by ado automation system user");
                    }
                    else if (selfChangedDic.Count > 0 && selfChangedDicCheck.Count == 0)
                    {
                        _logger.Debug("Will not execute script since irrelevant fields were updated");

                    }
                    else
                    {
                        //var engine = new CSharpScriptEngine(_logger);
                        err = await _scriptEngine.ExecuteScripts(_context);
                    }
                    //}
                }
                else
                {
                    //var engine = new CSharpScriptEngine(_logger);
                    err = await _scriptEngine.ExecuteScripts(_context);
                }

            }
            catch (Exception ex)
            {
                _logger.Error($"WebHook handler failed: {ex.Message}");
                throw;
            }
            return err;
        }
    }
}
