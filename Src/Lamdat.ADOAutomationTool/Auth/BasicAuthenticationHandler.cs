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

        /// <summary>
        /// Sanitizes user input for logging by removing newlines and carriage returns to prevent log injection
        /// </summary>
        /// <param name="input">The input string to sanitize</param>
        /// <returns>Sanitized string safe for logging</returns>
        private static string SanitizeForLogging(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;
            
            return input.Replace("\n", "").Replace("\r", "");
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            try
            {
                if (!Request.Headers.ContainsKey(_authorizationHeaderName))
                {
                    Logger.LogDebug("Authorization header missing for {RequestPath}", Request.Path);
                    return AuthenticateResult.NoResult();
                }

                var authorizationHeader = Request.Headers[_authorizationHeaderName].ToString();
                if (string.IsNullOrWhiteSpace(authorizationHeader))
                {
                    Logger.LogWarning("Empty authorization header for {RequestPath}", Request.Path);
                    return AuthenticateResult.NoResult();
                }

                if (!authorizationHeader.StartsWith(_basicSchemeName + " ", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.LogWarning("Invalid authentication scheme. Expected: {ExpectedScheme}, Actual: {ActualScheme} for {RequestPath}", 
                        _basicSchemeName, authorizationHeader.Split(' ').FirstOrDefault(), Request.Path);
                    return AuthenticateResult.NoResult();
                }

                var encodedCredentials = authorizationHeader.Substring(_basicSchemeName.Length).Trim();
                if (string.IsNullOrWhiteSpace(encodedCredentials))
                {
                    Logger.LogWarning("Empty credentials in authorization header for {RequestPath}", Request.Path);
                    return AuthenticateResult.Fail("Invalid credentials format");
                }

                string decodedCredentials;
                try
                {
                    decodedCredentials = Encoding.UTF8.GetString(Convert.FromBase64String(encodedCredentials));
                }
                catch (FormatException ex)
                {
                    Logger.LogWarning(ex, "Invalid base64 encoding in authorization header for {RequestPath}", Request.Path);
                    return AuthenticateResult.Fail("Invalid credentials encoding");
                }

                var credentials = decodedCredentials.Split(':', 2);
                if (credentials.Length != 2)
                {
                    Logger.LogWarning("Invalid credentials format. Expected 'username:password' for {RequestPath}", Request.Path);
                    return AuthenticateResult.Fail("Invalid credentials format");
                }
                
                var username = credentials[0];
                var password = credentials[1];

                // Sanitize username before logging to prevent log injection attacks
                var sanitizedUsername = SanitizeForLogging(username);
                Logger.LogDebug("Authentication attempt for user: {Username} on {RequestPath}", sanitizedUsername, Request.Path);
                
                // Critical: Check if SharedKey is available
                if (string.IsNullOrWhiteSpace(_sharedKey))
                {
                    Logger.LogError("SharedKey is not configured - this will cause 403 errors! Request: {RequestPath}", Request.Path);
                    return AuthenticateResult.Fail("Server configuration error");
                }

                if (password != _sharedKey)
                {
                    Logger.LogWarning("Authentication failed for user: {Username}. Password mismatch for {RequestPath}. Expected key length: {ExpectedLength}, Actual length: {ActualLength}", 
                        sanitizedUsername, Request.Path, _sharedKey.Length, password.Length);
                    return AuthenticateResult.Fail("Invalid username or password");
                }

                var claims = new[] { new Claim(ClaimTypes.Name, username) };
                var identity = new ClaimsIdentity(claims, Scheme.Name);
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, Scheme.Name);

                Logger.LogDebug("Authentication successful for user: {Username} on {RequestPath}", sanitizedUsername, Request.Path);
                return AuthenticateResult.Success(ticket);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected error during authentication for {RequestPath}", Request.Path);
                return AuthenticateResult.Fail("Authentication error");
            }
        }

    }
}