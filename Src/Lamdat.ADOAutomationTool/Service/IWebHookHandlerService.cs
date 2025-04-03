
namespace Lamdat.ADOAutomationTool.Service
{
    public interface IWebHookHandlerService
    {
        Task<string?> HandleWebHook(string webHookBody);
        Task Init();
    }
}