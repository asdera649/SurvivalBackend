using Microsoft.AspNetCore.Mvc;

namespace SurvivalBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MainController : ControllerBase
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiBaseUrl;
        private readonly string _apiToken;

        public MainController(IConfiguration configuration)
        {
            _httpClient = new HttpClient();

            _apiBaseUrl = "https://api.edgegap.com/v1/deployments";
            _apiToken = "token 84baf80d-10ab-4fe6-8a5b-3733434583ae";

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("authorization", _apiToken);
        }

        [HttpGet("deployments")]
        public async Task<IActionResult> GetDeployments()
        {
            var response = await _httpClient.GetAsync(_apiBaseUrl);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return Content(content, "application/json");
            }
            else
            {
                return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());
            }
        }
    }
}
