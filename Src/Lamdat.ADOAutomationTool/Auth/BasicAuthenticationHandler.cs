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
            
            // Monitor for configuration changes that could affect authentication
            options.OnChange((newOptions, name) =>
            {
                var newSharedKey = newOptions.SharedKey;
                if (string.IsNullOrWhiteSpace(newSharedKey))
                {
                    Logger.LogError("CRITICAL: SharedKey configuration changed to null/empty! This will cause 403 errors!");
                }
                else if (newSharedKey != _sharedKey)
                {
                    Logger.LogInformation("SharedKey configuration updated");
                }
            });
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey(_authorizationHeaderName))
            {
                Logger.LogWarning("Authorization header missing");
                return AuthenticateResult.NoResult();
            }

            var authorizationHeader = Request.Headers[_authorizationHeaderName].ToString();
            if (!authorizationHeader.StartsWith(_basicSchemeName + " ", StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogWarning("Invalid authentication scheme. Expected: {ExpectedScheme}, Actual: {ActualScheme}", 
                    _basicSchemeName, authorizationHeader.Split(' ').FirstOrDefault());
                return AuthenticateResult.NoResult();
            }

            try
            {
                var encodedCredentials = authorizationHeader.Substring(_basicSchemeName.Length).Trim();
                var decodedCredentials = Encoding.UTF8.GetString(Convert.FromBase64String(encodedCredentials));
                var credentials = decodedCredentials.Split(':', 2);
                
                if (credentials.Length != 2)
                {
                    Logger.LogWarning("Invalid credentials format. Expected 'username:password'");
                    return AuthenticateResult.Fail("Invalid credentials format");
                }
                
                var username = credentials[0];
                var password = credentials[1];

                Logger.LogDebug("Authentication attempt for user: {Username}", username);
                
                if (string.IsNullOrWhiteSpace(_sharedKey))
                {
                    Logger.LogError("SharedKey is not configured");
                    return AuthenticateResult.Fail("Server configuration error");
                }

                if (password != _sharedKey)
                {
                    Logger.LogWarning("Invalid password for user: {Username}. Expected key length: {ExpectedLength}, Actual length: {ActualLength}", 
                        username, _sharedKey.Length, password.Length);
                    return AuthenticateResult.Fail("Invalid username or password");
                }

                var claims = new[] { new Claim(ClaimTypes.Name, username) };
                var identity = new ClaimsIdentity(claims, Scheme.Name);
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, Scheme.Name);

                Logger.LogInformation("Authentication successful for user: {Username}", username);
                return AuthenticateResult.Success(ticket);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error during authentication");
                return AuthenticateResult.Fail("Authentication error");
            }
        }

    }
}