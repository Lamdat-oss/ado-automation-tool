using Lamdat.ADOAutomationTool.Entities;
using Lamdat.ADOAutomationTool.ScriptEngine;
using Lamdat.ADOAutomationTool.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System.Text;

namespace Lamdat.ADOAutomationTool.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class WebHookController : ControllerBase
    {

        private readonly ILogger<WebHookController> _logger;
        private IWebHookHandlerService _handlerService;


        public WebHookController(ILogger<WebHookController> logger, IWebHookHandlerService handlerService)
        {
            _logger = logger;
            _handlerService = handlerService;
        }


        [HttpPost]
        public async Task<IActionResult> Handle()
        {
            try
            {
                // Log authentication details for debugging
                _logger.LogDebug("Webhook request authenticated as: {User}", User.Identity?.Name ?? "Anonymous");
                
                string body;
                
                // Check if there's a content-length header
                var contentLength = Request.ContentLength;
                _logger.LogDebug("Content-Length header: {ContentLength}", contentLength?.ToString() ?? "null");
                
                // If content length is 0 or null, return early
                if (contentLength == 0)
                {
                    _logger.LogWarning("Received webhook with Content-Length 0 from {RemoteIP} for user {User}", 
                        HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
                        User.Identity?.Name ?? "Anonymous");
                    return BadRequest("Empty webhook payload - Content-Length is 0");
                }
                
                // Enable buffering to allow multiple reads if needed
                Request.EnableBuffering();
                
                // Use a more robust approach to read the body
                using var memoryStream = new MemoryStream();
                await Request.Body.CopyToAsync(memoryStream);
                
                // Reset position for potential future reads
                Request.Body.Position = 0;
                
                // Convert to string
                body = Encoding.UTF8.GetString(memoryStream.ToArray());
                
                _logger.LogDebug("Successfully read {ActualBytes} bytes from request body", memoryStream.Length);

                if (string.IsNullOrWhiteSpace(body))
                {
                    _logger.LogWarning("Received empty or whitespace-only data in webhook from {RemoteIP} for user {User}", 
                        HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
                        User.Identity?.Name ?? "Anonymous");
                    return BadRequest("Empty webhook payload");
                }

                _logger.LogDebug("Webhook received from {RemoteIP} for user {User}, payload length: {Length}", 
                    HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown", 
                    User.Identity?.Name ?? "Anonymous",
                    body.Length);
                
                var err = await _handlerService.HandleWebHook(body);

                if (err != null)
                {
                    _logger.LogError("Webhook handler error: {Error} for user {User}", err, User.Identity?.Name ?? "Anonymous");
                    return StatusCode(503, new { error = "Webhook processing failed" });
                }
                else
                {
                    _logger.LogDebug("Webhook processed successfully for user {User}", User.Identity?.Name ?? "Anonymous");
                    return Ok(new { status = "success" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while processing webhook for user {User}", User.Identity?.Name ?? "Anonymous");
                return StatusCode(503, new { error = "Internal server error" });
            }
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Status()
        {
            var ok = new { Status = "ok" };
            return Ok(ok);
        }

        // Add a simple test endpoint that requires authentication
        [HttpGet("test")]
        public async Task<IActionResult> TestAuth()
        {
            return Ok(new { Status = "authenticated", User = User.Identity?.Name });
        }
    }
}

