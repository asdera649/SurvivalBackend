using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NodaTime;
using SurvivalBackend.Jobs;
using SurvivalBackend.Security;

namespace SurvivalBackend.Controllers;

[ApiController]
[Route("[controller]")]
[EnableRateLimiting(RateLimitPolicies.Public)]
public sealed class ServersWipeController(ServersWipeScheduler serversWipeScheduler) : ControllerBase
{
    private readonly ServersWipeScheduler _serversWipeScheduler = serversWipeScheduler;

    public sealed class RemainingTimeToWipe
    {
        public int Days { get; set; }
        public int Hours { get; set; }
        public int Minutes { get; set; }
    }

    [HttpGet("remainingTimeToWipe")]
    public IActionResult GetRemainingTimeToWipe()
    {
        var nextExecution = _serversWipeScheduler.GetNextExecutionDate();
        var duration = nextExecution.ToInstant() - SystemClock.Instance.GetCurrentInstant();
        var totalMinutes = Math.Max(0, (long)Math.Floor(duration.TotalMinutes));

        return Ok(new RemainingTimeToWipe
        {
            Days = (int)(totalMinutes / (24 * 60)),
            Hours = (int)(totalMinutes % (24 * 60) / 60),
            Minutes = (int)(totalMinutes % 60)
        });
    }
}
