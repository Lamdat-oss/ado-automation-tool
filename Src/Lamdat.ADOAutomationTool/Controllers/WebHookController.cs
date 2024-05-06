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
        private readonly Settings _settingsAccessor;

        public WebHookController(IOptions<Settings> settingsAccessor, ILogger<WebHookController> logger)
        {
            _logger = logger;
            _settingsAccessor = settingsAccessor.Value;
        }


        [HttpPost]
        public async Task<IActionResult> Handle()
        {
            try
            {
                using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
                {
                    string body = await reader.ReadToEndAsync();

                    if (string.IsNullOrWhiteSpace(body))
                    {
                        _logger.Log(LogLevel.Warning, "Received empty data in web hook");
                        return BadRequest();
                    }

                    var err = await new WebHookHandler(_logger, _settingsAccessor).HandleWebHook(body);

                    if (err != null)
                        return StatusCode(503);
                    else
                        return Ok();
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, ex.Message);
                return StatusCode(503);
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

