using Microsoft.AspNetCore.Authentication;

namespace Lamdat.ADOAutomationTool.Auth
{
    public class BasicAuthenticationOptions : AuthenticationSchemeOptions
    {
        public string SharedKey { get; set; }
    }

}
