using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SurvivalBackend.Options;

namespace SurvivalBackend.Infrastructure.Edgegap;

public sealed class EdgegapClient(
    IHttpClientFactory httpClientFactory,
    IOptions<EdgegapOptions> options,
    ILogger<EdgegapClient> logger) : IEdgegapClient
{
    public const string HttpClientName = "Edgegap";

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly EdgegapOptions _options = options.Value;
    private readonly ILogger<EdgegapClient> _logger = logger;

    public async Task<IReadOnlyList<EdgegapDeployment>> GetDeploymentsAsync(CancellationToken cancellationToken)
    {
        var content = await SendAsync(
            () => CreateRequest(HttpMethod.Get, "deployments"),
            cancellationToken);

        using var document = JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty("data", out var dataArray) || dataArray.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var deployments = new List<EdgegapDeployment>();
        foreach (var item in dataArray.EnumerateArray())
        {
            var requestId = GetString(item, "request_id");
            var publicIp = GetString(item, "public_ip");
            var ready = GetBool(item, "ready");

            if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(publicIp))
            {
                continue;
            }

            deployments.Add(new EdgegapDeployment(requestId, publicIp, ready));
        }

        return deployments;
    }

    public async Task<EdgegapDeploymentStatus> GetDeploymentStatusAsync(string requestId, CancellationToken cancellationToken)
    {
        var escapedRequestId = Uri.EscapeDataString(requestId);
        var content = await SendAsync(
            () => CreateRequest(HttpMethod.Get, $"status/{escapedRequestId}"),
            cancellationToken);

        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;

        int? gamePortExternal = null;
        if (root.TryGetProperty("ports", out var ports)
            && ports.TryGetProperty("gameport", out var gamePort)
            && gamePort.TryGetProperty("external", out var externalPort)
            && externalPort.TryGetInt32(out var parsedPort))
        {
            gamePortExternal = parsedPort;
        }

        return new EdgegapDeploymentStatus(
            GetString(root, "current_status"),
            GetBool(root, "running"),
            GetString(root, "public_ip"),
            gamePortExternal);
    }

    public async Task<IReadOnlyList<EdgegapFleet>> GetFleetsAsync(CancellationToken cancellationToken)
    {
        var content = await SendAsync(
            () => CreateRequest(HttpMethod.Get, "fleets"),
            cancellationToken);

        using var document = JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty("fleets", out var fleetsArray) || fleetsArray.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var fleets = new List<EdgegapFleet>();
        foreach (var item in fleetsArray.EnumerateArray())
        {
            var name = GetString(item, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            fleets.Add(new EdgegapFleet(name, GetBool(item, "enabled")));
        }

        return fleets;
    }

    public async Task SetFleetEnabledAsync(string fleetName, bool enabled, CancellationToken cancellationToken)
    {
        var escapedFleetName = Uri.EscapeDataString(fleetName);
        var body = JsonSerializer.Serialize(new { enabled });

        await SendAsync(
            () => CreateRequest(HttpMethod.Patch, $"fleet/{escapedFleetName}", body),
            cancellationToken);
    }

    public async Task BulkStopDeploymentsAsync(CancellationToken cancellationToken)
    {
        await SendAsync(
            () => CreateRequest(HttpMethod.Post, "deployments/bulk-stop", "{ \"filters\": [] }"),
            cancellationToken);
    }

    private async Task<string> SendAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        var attempts = Math.Max(1, _options.RetryAttempts);
        Exception? lastException = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            using var request = requestFactory();

            try
            {
                var client = _httpClientFactory.CreateClient(HttpClientName);
                using var response = await client.SendAsync(request, cancellationToken);
                var content = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return content;
                }

                if (IsTransient(response.StatusCode) && attempt < attempts)
                {
                    await DelayBeforeRetry(attempt, cancellationToken);
                    continue;
                }

                throw new EdgegapApiException(response.StatusCode, content);
            }
            catch (HttpRequestException exception) when (attempt < attempts)
            {
                lastException = exception;
                _logger.LogWarning(exception, "Transient Edgegap request failure on attempt {Attempt}.", attempt);
                await DelayBeforeRetry(attempt, cancellationToken);
            }
            catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested && attempt < attempts)
            {
                lastException = exception;
                _logger.LogWarning(exception, "Edgegap request timed out on attempt {Attempt}.", attempt);
                await DelayBeforeRetry(attempt, cancellationToken);
            }
        }

        throw new HttpRequestException("Edgegap request failed after retries.", lastException);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativeUrl, string? jsonBody = null)
    {
        var request = new HttpRequestMessage(method, relativeUrl);

        if (!string.IsNullOrWhiteSpace(_options.Token))
        {
            request.Headers.TryAddWithoutValidation("authorization", _options.Token);
        }

        if (jsonBody is not null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private Task DelayBeforeRetry(int attempt, CancellationToken cancellationToken)
    {
        var delayMs = _options.RetryBaseDelayMs * Math.Pow(2, attempt - 1);
        return Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken);
    }

    private static bool IsTransient(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool GetBool(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            && property.GetBoolean();
    }
}
