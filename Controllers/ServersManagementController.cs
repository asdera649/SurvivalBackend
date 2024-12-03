using Microsoft.AspNetCore.Mvc;
using SurvivalBackend.Utilities;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace SurvivalBackend.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ServersManagementController : ControllerBase
    {
        public class ServerInfo
        {
            public required string Ip { get; set; }
            public required string RequestId { get; set; }
            public required string Name { get; set; }
            public int MaxPlayersCount { get; set; }
            public int CurrentPlayersCount { get; set; }
        }

        public class ServerConnectionInfo
        {
            public required string PublicIp { get; set; }
            public int ExternalPort { get; set; }
        }

        public class ServerState
        {
            [Required] public int MaxPlayersCount { get; set; }
            [Required] public int CurrentPlayersCount { get; set; }
        }

        public ServersManagementController(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _configuration = configuration;
        }

        private Dictionary<string, string> _serverNamesCache = new();

        private Dictionary<string, ServerState> _serverPropertiesCache = new();

        private int _currentServerIndex = 1;

        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        [HttpGet("connect")]
        public async Task<IActionResult> ConnectToServer([FromQuery] string requestId, [FromQuery] string clientVersion)
        {
            if (clientVersion != ActualGameClientData.GetCurrentGameClientVersion())
            {
                return StatusCode(426, "Client version is outdated.");
            }

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("authorization", _configuration["EdgegapToken"]);

            var response = await _httpClient.GetAsync($"https://api.edgegap.com/v1/status/{requestId}");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();

                using var document = JsonDocument.Parse(content);

                var publicIp = document.RootElement.GetProperty("public_ip").GetString();

                if (string.IsNullOrEmpty(publicIp))
                {
                    return BadRequest("Unable to determine the IP address.");
                }

                var serverConnectionInfo = new ServerConnectionInfo
                {
                    PublicIp = publicIp,
                    ExternalPort = document.RootElement.GetProperty("ports")
                                                       .GetProperty("gameport")
                                                       .GetProperty("external")
                                                       .GetInt32()
                };

                return Ok(serverConnectionInfo);
            }
            else
            {
                return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());
            }
        }

        [HttpGet("servers")]
        public async Task<IActionResult> GetServers([FromQuery] string clientVersion)
        {
            if (clientVersion != ActualGameClientData.GetCurrentGameClientVersion())
            {
                return StatusCode(426, "Client version is outdated.");
            }

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("authorization", _configuration["EdgegapToken"]);

            var response = await _httpClient.GetAsync("https://api.edgegap.com/v1/deployments");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();

                var servers = new List<ServerInfo>();

                using var document = JsonDocument.Parse(content);

                var dataArray = document.RootElement.GetProperty("data");

                foreach (var s in dataArray.EnumerateArray())
                {
                    var requestId = s.GetProperty("request_id").GetString();
                    var ready = s.GetProperty("ready").GetBoolean();
                    var publicIp = s.GetProperty("public_ip").GetString();

                    if (string.IsNullOrEmpty(requestId) || !ready || string.IsNullOrEmpty(publicIp))
                    {
                        continue;
                    }

                    var name = GetName(requestId);

                    servers.Add(new ServerInfo
                    {
                        Ip = publicIp,
                        RequestId = requestId,
                        Name = name,
                        MaxPlayersCount = _serverPropertiesCache.TryGetValue(publicIp, out var state) ? state.MaxPlayersCount : 0,
                        CurrentPlayersCount = _serverPropertiesCache.TryGetValue(publicIp, out state) ? state.CurrentPlayersCount : 0
                    });
                }

                return Ok(servers);
            }
            else
            {
                return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());
            }
        }

        [HttpPost("updateServerState")]
        public IActionResult UpdateServerState([FromBody] ServerState serverState)
        {
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            if (string.IsNullOrEmpty(ipAddress))
            {
                return BadRequest("Unable to determine the sender's IP address.");
            }

            _serverPropertiesCache[ipAddress] = serverState;

            return Ok($"Server {ipAddress} state updated successfully.");
        }

        private string GetName(string requestId)
        {
            if (_serverNamesCache.TryGetValue(requestId, out var name))
            {
                return name;
            }
            else
            {
                _serverNamesCache.Add(requestId, "#" + _currentServerIndex + " Server");
                _currentServerIndex++;

                return _serverNamesCache[requestId];
            }
        }
    }
}
