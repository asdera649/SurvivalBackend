using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SurvivalBackend.Infrastructure.Edgegap;
using SurvivalBackend.Options;
using SurvivalBackend.Security;
using SurvivalBackend.Services;

namespace SurvivalBackend.Controllers;

[ApiController]
[Route("admin/api")]
[RequireApiKey(ApiKeyRole.Admin)]
[EnableRateLimiting(RateLimitPolicies.Admin)]
public sealed class AdminController(
    ServersListService serversListService,
    IEdgegapClient edgegapClient,
    IServersWipeService wipeService,
    IGameClientVersionProvider gameClientVersionProvider,
    IOptions<EdgegapOptions> edgegapOptions,
    IOptions<S3Options> s3Options,
    IOptions<SecurityOptions> securityOptions,
    IOptions<ServerRegistryOptions> registryOptions,
    ILogger<AdminController> logger) : ControllerBase
{
    private readonly ServersListService _serversListService = serversListService;
    private readonly IEdgegapClient _edgegapClient = edgegapClient;
    private readonly IServersWipeService _wipeService = wipeService;
    private readonly IGameClientVersionProvider _gameClientVersionProvider = gameClientVersionProvider;
    private readonly EdgegapOptions _edgegapOptions = edgegapOptions.Value;
    private readonly S3Options _s3Options = s3Options.Value;
    private readonly SecurityOptions _securityOptions = securityOptions.Value;
    private readonly ServerRegistryOptions _registryOptions = registryOptions.Value;
    private readonly ILogger<AdminController> _logger = logger;

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview(CancellationToken cancellationToken)
    {
        IReadOnlyList<EdgegapDeployment> deployments = [];
        string? edgegapError = null;

        try
        {
            deployments = await _edgegapClient.GetDeploymentsAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExternalServiceException(exception))
        {
            _logger.LogWarning(exception, "Admin overview could not load Edgegap deployments.");
            edgegapError = exception.Message;
        }

        var deploymentByRequestId = deployments
            .GroupBy(deployment => deployment.RequestId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var runtimeStates = _serversListService.GetRuntimeStatesSnapshot();
        var servers = _serversListService.GetServersSnapshot()
            .Select(server =>
            {
                runtimeStates.TryGetValue(server.RequestId, out var runtimeState);
                deploymentByRequestId.TryGetValue(server.RequestId, out var deployment);

                return new
                {
                    server.UniqueId,
                    server.ServerName,
                    server.RequestId,
                    server.Ready,
                    Runtime = runtimeState,
                    Edgegap = deployment
                };
            })
            .ToList();

        return Ok(new
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            GameClientVersion = _gameClientVersionProvider.CurrentVersion,
            Registry = new
            {
                ServersCount = servers.Count,
                _registryOptions.StorageMode
            },
            Edgegap = new
            {
                Status = edgegapError is null ? "Ok" : "Error",
                Error = edgegapError,
                DeploymentsCount = deployments.Count,
                ReadyDeploymentsCount = deployments.Count(deployment => deployment.Ready)
            },
            Wipe = _wipeService.CurrentState,
            Servers = servers
        });
    }

    [HttpPost("wipe/run")]
    public IActionResult RunWipe()
    {
        if (!_wipeService.TryStartInBackground("admin"))
        {
            return Conflict(new
            {
                Message = "Wipe is already running.",
                State = _wipeService.CurrentState
            });
        }

        return Accepted(new
        {
            Message = "Wipe started.",
            State = _wipeService.CurrentState
        });
    }

    [HttpPost("servers/release-missing")]
    public async Task<IActionResult> ReleaseMissingServers(CancellationToken cancellationToken)
    {
        try
        {
            var activeRequestIds = (await _edgegapClient.GetDeploymentsAsync(cancellationToken))
                .Where(deployment => deployment.Ready)
                .Select(deployment => deployment.RequestId)
                .ToHashSet(StringComparer.Ordinal);

            var releasedCount = await _serversListService.ReleaseMissingDeploymentsAsync(activeRequestIds, cancellationToken);
            return Ok(new { ReleasedCount = releasedCount });
        }
        catch (Exception exception) when (TryMapExternalException(exception, out var result))
        {
            return result;
        }
    }

    [HttpGet("config")]
    public IActionResult GetSanitizedConfig()
    {
        return Ok(new
        {
            Edgegap = new
            {
                _edgegapOptions.BaseUrl,
                _edgegapOptions.TimeoutSeconds,
                _edgegapOptions.RetryAttempts,
                _edgegapOptions.RetryBaseDelayMs,
                TokenConfigured = !string.IsNullOrWhiteSpace(_edgegapOptions.Token)
            },
            S3 = new
            {
                _s3Options.EndPoint,
                _s3Options.BucketName,
                _s3Options.CurrentWipeSavesPath,
                _s3Options.ServersListSavesPath,
                _s3Options.CredentialDeliveryMode,
                _s3Options.PresignedUrlExpirationMinutes,
                AccessKeyConfigured = !string.IsNullOrWhiteSpace(_s3Options.AccessKey),
                SecretKeyConfigured = !string.IsNullOrWhiteSpace(_s3Options.SecretKey)
            },
            Security = new
            {
                _securityOptions.RequireApiKeys,
                _securityOptions.ServerApiKeyHeaderName,
                _securityOptions.AdminApiKeyHeaderName,
                ServerApiKeyConfigured = !string.IsNullOrWhiteSpace(_securityOptions.ServerApiKey),
                AdminApiKeyConfigured = !string.IsNullOrWhiteSpace(_securityOptions.AdminApiKey)
            },
            ServerRegistry = _registryOptions
        });
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
            default:
                result = default!;
                return false;
        }
    }

    private static bool IsExternalServiceException(Exception exception)
    {
        return exception is EdgegapApiException or HttpRequestException or TaskCanceledException;
    }
}
