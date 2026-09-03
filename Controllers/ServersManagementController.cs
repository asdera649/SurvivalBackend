using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SurvivalBackend.Contracts;
using SurvivalBackend.Infrastructure.Edgegap;
using SurvivalBackend.Infrastructure.Storage;
using SurvivalBackend.Security;
using SurvivalBackend.Services;

namespace SurvivalBackend.Controllers;

[ApiController]
[Route("[controller]")]
public sealed class ServersManagementController(
    ServersListService serversListService,
    IEdgegapClient edgegapClient,
    ISaveStorage saveStorage,
    IGameClientVersionProvider gameClientVersionProvider,
    ILogger<ServersManagementController> logger) : ControllerBase
{
    private static readonly Regex RequestIdRegex = new("^[A-Za-z0-9._:-]{1,128}$", RegexOptions.Compiled);

    private readonly ServersListService _serversListService = serversListService;
    private readonly IEdgegapClient _edgegapClient = edgegapClient;
    private readonly ISaveStorage _saveStorage = saveStorage;
    private readonly IGameClientVersionProvider _gameClientVersionProvider = gameClientVersionProvider;
    private readonly ILogger<ServersManagementController> _logger = logger;

    [HttpGet("registerServer")]
    [RequireApiKey(ApiKeyRole.Server)]
    [EnableRateLimiting(RateLimitPolicies.Management)]
    public async Task<IActionResult> RegisterServer([FromQuery] string requestId, CancellationToken cancellationToken)
    {
        if (!IsValidRequestId(requestId))
        {
            return BadRequest("Invalid requestId.");
        }

        try
        {
            var activeDeployments = await GetReadyDeploymentsAsync(cancellationToken);
            var activeRequestIds = activeDeployments
                .Select(deployment => deployment.RequestId)
                .ToHashSet(StringComparer.Ordinal);

            if (!activeRequestIds.Contains(requestId))
            {
                return BadRequest("There is no such ready deployment.");
            }

            var server = await _serversListService.RegisterDeploymentAsync(requestId, activeRequestIds, cancellationToken);
            return Ok(_saveStorage.CreateServerSaveAccess(server.ServerName));
        }
        catch (Exception exception) when (TryMapExternalException(exception, out var result))
        {
            return result;
        }
    }

    [HttpPost("renewSaveAccess")]
    [RequireApiKey(ApiKeyRole.Server)]
    [EnableRateLimiting(RateLimitPolicies.Management)]
    public IActionResult RenewSaveAccess([FromQuery] string requestId)
    {
        if (!IsValidRequestId(requestId))
        {
            return BadRequest("Invalid requestId.");
        }

        var server = _serversListService.GetServersSnapshot()
            .FirstOrDefault(item => item.RequestId == requestId);

        if (server is null)
        {
            return NotFound("Unable to determine the server.");
        }

        return Ok(_saveStorage.CreateServerSaveAccess(server.ServerName));
    }

    [HttpPost("setServerReady")]
    [RequireApiKey(ApiKeyRole.Server)]
    [EnableRateLimiting(RateLimitPolicies.Management)]
    public async Task<IActionResult> SetServerReady([FromQuery] string requestId, CancellationToken cancellationToken)
    {
        if (!IsValidRequestId(requestId))
        {
            return BadRequest("Invalid requestId.");
        }

        var updated = await _serversListService.MarkReadyAsync(requestId, cancellationToken);
        return updated ? Ok() : NotFound("Unable to determine the server.");
    }

    [HttpGet("connect")]
    [EnableRateLimiting(RateLimitPolicies.Public)]
    public async Task<IActionResult> ConnectToServer(
        [FromQuery] string uniqueId,
        [FromQuery] string clientVersion,
        CancellationToken cancellationToken)
    {
        if (!IsClientVersionSupported(clientVersion))
        {
            return StatusCode(StatusCodes.Status426UpgradeRequired, "Client version is outdated.");
        }

        if (!Guid.TryParse(uniqueId, out _))
        {
            return BadRequest("Invalid uniqueId.");
        }

        var server = _serversListService.GetServersSnapshot()
            .FirstOrDefault(item => item.UniqueId == uniqueId);

        if (server is null)
        {
            return BadRequest("Unable to determine the server.");
        }

        if (!server.Ready)
        {
            return BadRequest("Server app is not ready.");
        }

        try
        {
            var status = await _edgegapClient.GetDeploymentStatusAsync(server.RequestId, cancellationToken);

            if (status.CurrentStatus != "Status.READY")
            {
                return BadRequest("Server is not ready.");
            }

            if (!status.Running)
            {
                return BadRequest("Server is not running.");
            }

            if (string.IsNullOrWhiteSpace(status.PublicIp))
            {
                return BadRequest("Unable to determine the IP address.");
            }

            if (status.GamePortExternal is null)
            {
                return BadRequest("Unable to determine the game port.");
            }

            return Ok(new ServerConnectionInfo
            {
                PublicIp = status.PublicIp,
                ExternalPort = status.GamePortExternal.Value
            });
        }
        catch (Exception exception) when (TryMapExternalException(exception, out var result))
        {
            return result;
        }
    }

    [HttpGet("servers")]
    [EnableRateLimiting(RateLimitPolicies.Public)]
    public async Task<IActionResult> GetServers([FromQuery] string clientVersion, CancellationToken cancellationToken)
    {
        if (!IsClientVersionSupported(clientVersion))
        {
            return StatusCode(StatusCodes.Status426UpgradeRequired, "Client version is outdated.");
        }

        try
        {
            var deployments = await GetReadyDeploymentsAsync(cancellationToken);
            var deploymentByRequestId = deployments
                .GroupBy(deployment => deployment.RequestId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            var output = new List<ServerInfo>();
            foreach (var server in _serversListService.GetServersSnapshot().Where(server => server.Ready))
            {
                if (!deploymentByRequestId.TryGetValue(server.RequestId, out var deployment))
                {
                    continue;
                }

                var maxPlayersCount = 0;
                var currentPlayersCount = 0;
                if (_serversListService.TryGetRuntimeState(server.RequestId, out var runtimeState))
                {
                    maxPlayersCount = runtimeState.MaxPlayersCount;
                    currentPlayersCount = runtimeState.CurrentPlayersCount;
                }

                output.Add(new ServerInfo
                {
                    Ip = deployment.PublicIp,
                    UniqueId = server.UniqueId,
                    Name = server.ServerName,
                    MaxPlayersCount = maxPlayersCount,
                    CurrentPlayersCount = currentPlayersCount
                });
            }

            return Ok(output);
        }
        catch (Exception exception) when (TryMapExternalException(exception, out var result))
        {
            return result;
        }
    }

    [HttpPost("updateServerState")]
    [RequireApiKey(ApiKeyRole.Server)]
    [EnableRateLimiting(RateLimitPolicies.Management)]
    public IActionResult UpdateServerState([FromQuery] string requestId, [FromBody] ServerState? serverState)
    {
        if (!IsValidRequestId(requestId))
        {
            return BadRequest("Invalid requestId.");
        }

        if (serverState is null)
        {
            return BadRequest("Server state body is required.");
        }

        if (serverState.CurrentPlayersCount > serverState.MaxPlayersCount)
        {
            return BadRequest("CurrentPlayersCount cannot be greater than MaxPlayersCount.");
        }

        if (_serversListService.GetServersSnapshot().All(server => server.RequestId != requestId))
        {
            return NotFound("Unable to determine the server.");
        }

        try
        {
            _serversListService.UpdateRuntimeState(requestId, serverState);
            return Ok($"Server {requestId} state updated successfully.");
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private async Task<IReadOnlyList<EdgegapDeployment>> GetReadyDeploymentsAsync(CancellationToken cancellationToken)
    {
        var deployments = await _edgegapClient.GetDeploymentsAsync(cancellationToken);
        return deployments
            .Where(deployment => deployment.Ready
                && !string.IsNullOrWhiteSpace(deployment.RequestId)
                && !string.IsNullOrWhiteSpace(deployment.PublicIp))
            .ToList();
    }

    private bool IsClientVersionSupported(string? clientVersion)
    {
        return !string.IsNullOrWhiteSpace(clientVersion)
            && string.Equals(clientVersion, _gameClientVersionProvider.CurrentVersion, StringComparison.Ordinal);
    }

    private static bool IsValidRequestId(string? requestId)
    {
        return !string.IsNullOrWhiteSpace(requestId) && RequestIdRegex.IsMatch(requestId);
    }

    private bool TryMapExternalException(Exception exception, out IActionResult result)
    {
        switch (exception)
        {
            case EdgegapApiException edgegapException:
                result = StatusCode((int)edgegapException.StatusCode, edgegapException.ResponseContent);
                return true;
            case HttpRequestException:
            case TaskCanceledException:
                result = StatusCode(StatusCodes.Status503ServiceUnavailable, "External service is unavailable.");
                return true;
            case JsonException:
                _logger.LogError(exception, "External service returned an unexpected JSON shape.");
                result = StatusCode(StatusCodes.Status502BadGateway, "External service returned invalid data.");
                return true;
            default:
                result = default!;
                return false;
        }
    }
}