using System.Text.Json;


namespace Lamdat.ADOAutomationTool.Service
{

    public static class CloneHelper
    {
        public static T DeepClone<T>(T obj)
        {
            var json = JsonSerializer.Serialize(obj);
            return JsonSerializer.Deserialize<T>(json);
        }
    }

}
