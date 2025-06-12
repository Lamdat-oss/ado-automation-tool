using System;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Lamdat.ADOAutomationTool.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lamdat.ADOAutomationTool.Auth
{
    public class BasicAuthenticationHandler : AuthenticationHandler<BasicAuthenticationOptions>
    {
        private const string _authorizationHeaderName = "Authorization";
        private const string _basicSchemeName = "Basic";
        private readonly string _sharedKey;

        public BasicAuthenticationHandler(
         IOptionsMonitor<BasicAuthenticationOptions> options,
         ILoggerFactory logger,
         UrlEncoder encoder)
         : base(options, logger, encoder)
        {
            _sharedKey = options.CurrentValue.SharedKey;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey(_authorizationHeaderName))
            {
                Logger.LogDebug("Authorization header missing");
                return AuthenticateResult.NoResult();
            }

            var authorizationHeader = Request.Headers[_authorizationHeaderName].ToString();
            if (!authorizationHeader.StartsWith(_basicSchemeName + " ", StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogInformation("Invalid authentication scheme");
                return AuthenticateResult.NoResult();
            }

            var encodedCredentials = authorizationHeader.Substring(_basicSchemeName.Length).Trim();
            var decodedCredentials = Encoding.UTF8.GetString(Convert.FromBase64String(encodedCredentials));
            var credentials = decodedCredentials.Split(':', 2);
            var username = credentials[0];
            var password = credentials[1];

            if (password != _sharedKey)
            {
                Logger.LogInformation("Invalid username or password");
                return AuthenticateResult.Fail("Invalid username or password");
            }

            var claims = new[] { new Claim(ClaimTypes.Name, username) };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            Logger.LogDebug("Authentication successful");
            return AuthenticateResult.Success(ticket);
        }

    }
}