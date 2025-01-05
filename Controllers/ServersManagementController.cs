using Microsoft.AspNetCore.Mvc;
using SurvivalBackend.Services;
using SurvivalBackend.Utilities;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using static SurvivalBackend.Services.ServersListService;

namespace SurvivalBackend.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ServersManagementController(
        ServersListService serversListService,
        HttpClient httpClient,
        IConfiguration configuration) : ControllerBase
    {
        #region Structs

        public class ServerInfo
        {
            public required string Ip { get; set; }
            public required string UniqueId { get; set; }
            public required string Name { get; set; }
            public int MaxPlayersCount { get; set; }
            public int CurrentPlayersCount { get; set; }
        }

        public class DeploymentInfo
        {
            public required string Ip { get; set; }
            public required string RequestId { get; set; }
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

        public class ServerRegistrationData
        {
            public required string EndPoint { get; set; }
            public required string BucketName { get; set; }
            public required string ObjectKey { get; set; }
            public required string AccessKey { get; set; }
            public required string SecretKey { get; set; }
        }

        #endregion

        private readonly static Dictionary<string, ServerState> _serversPropertiesCache = [];

        private readonly static List<string> _listOfPossibleServerNames = [
            "#1 Server",
            "#2 Server",
            "#3 Server",
            "#4 Server",
            "#5 Server",
            "#6 Server",
            "#7 Server",
            "#8 Server",
            "#9 Server",
            "#10 Server",
            "#11 Server",
            "#12 Server",
            "#13 Server",
            "#14 Server",
            "#15 Server"
            ];

        private readonly ServersListService _serversListService = serversListService;
        private readonly HttpClient _httpClient = httpClient;
        private readonly IConfiguration _configuration = configuration;

        #region ServerRegistrationStage

        [HttpGet("registerServer")]
        public async Task<IActionResult> RegisterServer([FromQuery] string requestId)
        {
            (bool isSuccessStatusCode, int statusCode, string content, List<DeploymentInfo> deploymentsList) deployments =
                await GetDeployments();

            await ReleaseServerContainers(deployments.deploymentsList);

            if (deployments.deploymentsList.All(s => s.RequestId != requestId))
            {
                return BadRequest("There is no such deployment.");
            }

            string serverName;

            foreach (var s in _serversListService.Items) // Если уже существует...
            {
                if (s.RequestId == requestId)
                {
                    serverName = s.ServerName;
                    goto sending;
                }
            }

            for (int i = 0; i < _serversListService.Items.Count; i++) // Ищем свободный...
            {
                var s = _serversListService.Items[i];

                if (s.RequestId == "null")
                {
                    serverName = s.ServerName;

                    await _serversListService.Edit(i, new ServerContainer(s.UniqueId, s.ServerName, requestId, s.Ready));

                    goto sending;
                }
            }

            // Создаем новый....

            serverName = GetNewName();

            await _serversListService.Add(new ServerContainer(Guid.NewGuid().ToString(), serverName, requestId, false));

        sending:

#pragma warning disable CS8601 // Возможно, назначение-ссылка, допускающее значение NULL.

            var serverRegistrationData = new ServerRegistrationData
            {
                EndPoint = _configuration["S3EndPoint"],
                BucketName = _configuration["S3BucketName"],
                ObjectKey = _configuration["S3CurrentWipeSavesPath"] + serverName + ".json",
                AccessKey = _configuration["S3AccessKey"],
                SecretKey = _configuration["S3SecretKey"],
            };

#pragma warning restore CS8601 // Возможно, назначение-ссылка, допускающее значение NULL.

            return Ok(serverRegistrationData);
        }

        private async Task ReleaseServerContainers(List<DeploymentInfo> deployments)
        {
            for (int i = 0; i < _serversListService.Items.Count; i++) 
            {
                ServerContainer s = _serversListService.Items[i];

                if (deployments.All(i => i.RequestId != s.RequestId))
                {
                    await _serversListService.Edit(i, new ServerContainer(s.UniqueId, s.ServerName, "null", false));
                }
            }
        }

        private string GetNewName()
        {
            foreach (var n in _listOfPossibleServerNames)
            {
                if (_serversListService.Items.All(i => i.ServerName != n))
                {
                    return n;
                }
            }

            return "Server(" + Guid.NewGuid().ToString() + ")";
        }

        [HttpPost("setServerReady")]
        public async Task<IActionResult> SetServerReady([FromQuery] string requestId)
        {
            for (int i = 0; i < _serversListService.Items.Count; i++)
            {
                ServerContainer s = _serversListService.Items[i];

                if (s.RequestId == requestId)
                {
                    await _serversListService.Edit(i, new ServerContainer(s.UniqueId, s.ServerName, s.RequestId, true));

                    return Ok();
                }
            }

            return BadRequest();
        }

        #endregion

        #region ConnectionStage

        [HttpGet("connect")]
        public async Task<IActionResult> ConnectToServer([FromQuery] string uniqueId, [FromQuery] string clientVersion)
        {
            if (clientVersion != ActualGameClientData.GetCurrentGameClientVersion())
            {
                return StatusCode(426, "Client version is outdated.");
            }

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("authorization", _configuration["EdgegapToken"]);

            ServerContainer? serverContainer = null;

            foreach (var s in _serversListService.Items)
            {
                if (s.UniqueId == uniqueId)
                {
                    serverContainer = s;
                    break;
                }
            }

            if (serverContainer == null)
            {
                return BadRequest("Unable to determine the server.");
            }

            if (!serverContainer.Value.Ready)
            {
                return BadRequest("Server app don't ready.");
            }

            HttpResponseMessage response;

            try
            {
                response = await _httpClient.GetAsync($"https://api.edgegap.com/v1/status/{serverContainer.Value.RequestId}");
            }
            catch (HttpRequestException)
            {
                response = new HttpResponseMessage
                {
                    StatusCode = System.Net.HttpStatusCode.ServiceUnavailable,
                    Content = new StringContent("Service unavailable due to network issues.")
                };
            }
            catch (TaskCanceledException)
            {
                response = new HttpResponseMessage
                {
                    StatusCode = System.Net.HttpStatusCode.RequestTimeout,
                    Content = new StringContent("Request timed out.")
                };
            }
            catch (Exception)
            {
                response = new HttpResponseMessage
                {
                    StatusCode = System.Net.HttpStatusCode.InternalServerError,
                    Content = new StringContent("An unexpected error occurred.")
                };
            }

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();

                using var document = JsonDocument.Parse(content);

                var currentStatus = document.RootElement.GetProperty("current_status").GetString();
                var running = document.RootElement.GetProperty("running").GetBoolean();
                var publicIp = document.RootElement.GetProperty("public_ip").GetString();
 
                if (currentStatus != "Status.READY")
                {
                    return BadRequest("Server don't ready.");
                }

                if (!running)
                {
                    return BadRequest("Server don't running.");
                }

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

            var response = await GetDeployments();

            if (response.isSuccessStatusCode)
            {
                var output = new List<ServerInfo>();

                foreach (var s in _serversListService.Items)
                {
                    if (!s.Ready)
                    {
                        continue;
                    }

                    foreach (var d in response.deployments)
                    {
                        if (s.RequestId == d.RequestId)
                        {
                            int maxPlayersCount = 0;
                            int currentPlayersCount = 0;

                            if (_serversPropertiesCache.TryGetValue(d.RequestId, out var serverState))
                            {
                                maxPlayersCount = serverState.MaxPlayersCount;
                                currentPlayersCount = serverState.CurrentPlayersCount;
                            }

                            output.Add(new ServerInfo()
                            {
                                Ip = d.Ip,
                                UniqueId = s.UniqueId,
                                Name = s.ServerName,
                                MaxPlayersCount = maxPlayersCount,
                                CurrentPlayersCount = currentPlayersCount
                            });

                            break;
                        }
                    }
                }

                return Ok(output);
            }
            else
            {
                return StatusCode(response.statusCode, response.content);
            }
        }

        private async Task<(bool isSuccessStatusCode, int statusCode, string content, List<DeploymentInfo> deployments)> GetDeployments()
        {
            var deploymentsList = new List<DeploymentInfo>();

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("authorization", _configuration["EdgegapToken"]);

            HttpResponseMessage response;

            try
            {
                response = await _httpClient.GetAsync("https://api.edgegap.com/v1/deployments");
            }
            catch (HttpRequestException)
            {
                response = new HttpResponseMessage
                {
                    StatusCode = System.Net.HttpStatusCode.ServiceUnavailable,
                    Content = new StringContent("Service unavailable due to network issues.")
                };
            }
            catch (TaskCanceledException)
            {
                response = new HttpResponseMessage
                {
                    StatusCode = System.Net.HttpStatusCode.RequestTimeout,
                    Content = new StringContent("Request timed out.")
                };
            }
            catch (Exception)
            {
                response = new HttpResponseMessage
                {
                    StatusCode = System.Net.HttpStatusCode.InternalServerError,
                    Content = new StringContent("An unexpected error occurred.")
                };
            }

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();

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

                    deploymentsList.Add(new DeploymentInfo
                    {
                        Ip = publicIp,
                        RequestId = requestId
                    });
                }
            }

            return (response.IsSuccessStatusCode, (int)response.StatusCode, await response.Content.ReadAsStringAsync(), deploymentsList);
        }

        #endregion

        #region ServerStateUpdateStage

        [HttpPost("updateServerState")]
        public IActionResult UpdateServerState([FromQuery] string requestId, [FromBody] ServerState serverState)
        {
            if (string.IsNullOrEmpty(requestId))
            {
                return BadRequest("Bad requestId, unable to determine the sender.");
            }

            _serversPropertiesCache[requestId] = serverState;

            return Ok($"Server {requestId} state updated successfully.");
        }

        #endregion
    }
}
