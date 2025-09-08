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
                
                // Enable buffering to allow multiple reads of the request body
                Request.EnableBuffering();
                
                using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true))
                {
                    string body = await reader.ReadToEndAsync();
                    
                    // Reset the stream position for potential future reads
                    Request.Body.Position = 0;

                    if (string.IsNullOrWhiteSpace(body))
                    {
                        _logger.LogWarning("Received empty data in web hook from {RemoteIP} for user {User}", 
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

        
    }
}

