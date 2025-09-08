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
                // Log request details for debugging intermittent issues
                Logger.LogDebug("Authentication attempt for {Method} {Path} from {RemoteIP}", 
                    Request.Method, Request.Path, Context.Connection.RemoteIpAddress?.ToString() ?? "Unknown");

                if (!Request.Headers.ContainsKey(_authorizationHeaderName))
                {
                    Logger.LogWarning("Authorization header missing for {Method} {Path} from {RemoteIP}", 
                        Request.Method, Request.Path, Context.Connection.RemoteIpAddress?.ToString() ?? "Unknown");
                    return AuthenticateResult.Fail("Authorization header missing");
                }

                var authorizationHeader = Request.Headers[_authorizationHeaderName].ToString();
                if (string.IsNullOrWhiteSpace(authorizationHeader))
                {
                    Logger.LogWarning("Empty authorization header for {Method} {Path} from {RemoteIP}", 
                        Request.Method, Request.Path, Context.Connection.RemoteIpAddress?.ToString() ?? "Unknown");
                    return AuthenticateResult.Fail("Empty authorization header");
                }

                if (!authorizationHeader.StartsWith(_basicSchemeName + " ", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.LogWarning("Invalid authentication scheme. Expected: {ExpectedScheme}, Received: {ReceivedScheme} for {Method} {Path} from {RemoteIP}", 
                        _basicSchemeName, authorizationHeader.Split(' ').FirstOrDefault(), Request.Method, Request.Path, Context.Connection.RemoteIpAddress?.ToString() ?? "Unknown");
                    return AuthenticateResult.Fail("Invalid authentication scheme");
                }

                var encodedCredentials = authorizationHeader.Substring(_basicSchemeName.Length).Trim();
                if (string.IsNullOrWhiteSpace(encodedCredentials))
                {
                    Logger.LogWarning("Empty credentials in authorization header for {Method} {Path} from {RemoteIP}", 
                        Request.Method, Request.Path, Context.Connection.RemoteIpAddress?.ToString() ?? "Unknown");
                    return AuthenticateResult.Fail("Empty credentials");
                }

                string decodedCredentials;
                try
                {
                    decodedCredentials = Encoding.UTF8.GetString(Convert.FromBase64String(encodedCredentials));
                }
                catch (FormatException ex)
                {
                    Logger.LogWarning(ex, "Invalid base64 encoding in authorization header for {Method} {Path} from {RemoteIP}. Encoded length: {Length}", 
                        Request.Method, Request.Path, Context.Connection.RemoteIpAddress?.ToString() ?? "Unknown", encodedCredentials.Length);
                    return AuthenticateResult.Fail("Invalid credentials encoding");
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Unexpected error decoding credentials for {Method} {Path} from {RemoteIP}", 
                        Request.Method, Request.Path, Context.Connection.RemoteIpAddress?.ToString() ?? "Unknown");
                    return AuthenticateResult.Fail("Credentials decoding error");
                }

                var credentials = decodedCredentials.Split(':', 2);
                if (credentials.Length != 2)
                {
                    Logger.LogWarning("Invalid credentials format. Expected 'username:password' for {Method} {Path} from {RemoteIP}. Actual parts: {PartsCount}", 
                        Request.Method, Request.Path, Context.Connection.RemoteIpAddress?.ToString() ?? "Unknown", credentials.Length);
                    return AuthenticateResult.Fail("Invalid credentials format");
                }
                
                var username = credentials[0];
                var password = credentials[1];

                // Sanitize username before logging to prevent log injection attacks
                var sanitizedUsername = SanitizeForLogging(username);
                
                // Critical: Check if SharedKey is available
                if (string.IsNullOrWhiteSpace(_sharedKey))
                {
                    Logger.LogError("SharedKey is not configured - this will cause 403 errors! Request: {Method} {Path} from {RemoteIP}", 
                        Request.Method, Request.Path, Context.Connection.RemoteIpAddress?.ToString() ?? "Unknown");
                    return AuthenticateResult.Fail("Server configuration error");
                }

                if (password != _sharedKey)
                {
                    Logger.LogWarning("Authentication failed for user: {Username}. Password mismatch for {Method} {Path} from {RemoteIP}. Expected key length: {ExpectedLength}, Actual length: {ActualLength}", 
                        sanitizedUsername, Request.Method, Request.Path, Context.Connection.RemoteIpAddress?.ToString() ?? "Unknown", _sharedKey.Length, password.Length);
                    return AuthenticateResult.Fail("Invalid username or password");
                }

                var claims = new[] { new Claim(ClaimTypes.Name, username) };
                var identity = new ClaimsIdentity(claims, Scheme.Name);
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, Scheme.Name);

                Logger.LogDebug("Authentication successful for user: {Username} on {Method} {Path} from {RemoteIP}", 
                    sanitizedUsername, Request.Method, Request.Path, Context.Connection.RemoteIpAddress?.ToString() ?? "Unknown");
                return AuthenticateResult.Success(ticket);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected error during authentication for {Method} {Path} from {RemoteIP}", 
                    Request.Method, Request.Path, Context.Connection.RemoteIpAddress?.ToString() ?? "Unknown");
                return AuthenticateResult.Fail("Authentication error");
            }
        }

    }
}