using Microsoft.AspNetCore.Mvc;
using SurvivalBackend.Utilities;

namespace SurvivalBackend.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ActualGameClientDataController : ControllerBase
    {
        [HttpGet("currentVersion")]
        public IActionResult GetCurrentGameClientVersion()
        {
            return Ok(ActualGameClientData.GetCurrentGameClientVersion());
        }
    }
}
