using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SurvivalBackend.Security;
using SurvivalBackend.Services;

namespace SurvivalBackend.Controllers;

[ApiController]
[Route("[controller]")]
[EnableRateLimiting(RateLimitPolicies.Public)]
public sealed class ActualGameClientDataController(IGameClientVersionProvider gameClientVersionProvider) : ControllerBase
{
    private readonly IGameClientVersionProvider _gameClientVersionProvider = gameClientVersionProvider;

    [HttpGet("currentVersion")]
    public IActionResult GetCurrentGameClientVersion()
    {
        return Ok(_gameClientVersionProvider.CurrentVersion);
    }
}
