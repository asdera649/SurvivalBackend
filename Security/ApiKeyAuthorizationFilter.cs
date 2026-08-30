using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using SurvivalBackend.Options;

namespace SurvivalBackend.Security;

public sealed class ApiKeyAuthorizationFilter(
    ApiKeyRole role,
    IOptions<SecurityOptions> securityOptions,
    ILogger<ApiKeyAuthorizationFilter> logger) : IAsyncAuthorizationFilter
{
    private readonly ApiKeyRole _role = role;
    private readonly SecurityOptions _securityOptions = securityOptions.Value;
    private readonly ILogger<ApiKeyAuthorizationFilter> _logger = logger;

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (!_securityOptions.RequireApiKeys)
        {
            return Task.CompletedTask;
        }

        var isAuthorized = _role switch
        {
            ApiKeyRole.Admin => HasMatchingHeader(context, _securityOptions.AdminApiKeyHeaderName, _securityOptions.AdminApiKey),
            ApiKeyRole.Server => HasMatchingHeader(context, _securityOptions.ServerApiKeyHeaderName, _securityOptions.ServerApiKey)
                || HasMatchingHeader(context, _securityOptions.AdminApiKeyHeaderName, _securityOptions.AdminApiKey),
            _ => false
        };

        if (!isAuthorized)
        {
            _logger.LogWarning(
                "Rejected {Role} API request from {RemoteIp} to {Path}.",
                _role,
                context.HttpContext.Connection.RemoteIpAddress,
                context.HttpContext.Request.Path);

            context.Result = new UnauthorizedObjectResult("Invalid or missing API key.");
        }

        return Task.CompletedTask;
    }

    private bool HasMatchingHeader(AuthorizationFilterContext context, string headerName, string expectedApiKey)
    {
        if (string.IsNullOrWhiteSpace(expectedApiKey))
        {
            return false;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue(headerName, out var providedValues))
        {
            return false;
        }

        var providedApiKey = providedValues.FirstOrDefault();
        return FixedTimeEquals(expectedApiKey, providedApiKey);
    }

    private static bool FixedTimeEquals(string expected, string? provided)
    {
        if (string.IsNullOrEmpty(provided))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var providedBytes = Encoding.UTF8.GetBytes(provided);

        return expectedBytes.Length == providedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}
