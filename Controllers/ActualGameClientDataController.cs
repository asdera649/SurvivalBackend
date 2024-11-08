using Microsoft.AspNetCore.Mvc;
using SurvivalBackend.Utilities;

namespace SurvivalBackend.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ActualGameClientDataController : ControllerBase
    {
        public ActualGameClientDataController(IConfiguration configuration)
        {
            _httpClient = new HttpClient();

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("authorization", configuration["EdgegapToken"]);
        }

        private readonly HttpClient _httpClient;

        [HttpGet("currentVersion")]
        public IActionResult GetCurrentGameClientVersion()
        {
            return Ok(ActualGameClientData.GetCurrentGameClientVersion());
        }
    }
}
