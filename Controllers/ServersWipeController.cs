using Microsoft.AspNetCore.Mvc;
using SurvivalBackend.Jobs;

namespace SurvivalBackend.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ServersWipeController : ControllerBase
    {
        public class RemainingTimeToWipe
        {
            public int Days { get; set; }
            public int Hours { get; set; }
            public int Minutes { get; set; }
        }

        public ServersWipeController(ServersWipeScheduler serversWipeScheduler) 
        {
            _serversWipeScheduler = serversWipeScheduler;
        }

        private readonly ServersWipeScheduler _serversWipeScheduler;

        [HttpGet("remainingTimeToWipe")]
        public IActionResult GetRemainingTimeToWipe()
        {
            var remainingTime = new RemainingTimeToWipe();

            var now = DateTime.Now;

            int daysUntilTarget = ((int)_serversWipeScheduler.WipeDayOfWeek - (int)now.DayOfWeek + 7) % 7;

            if (daysUntilTarget == 0 && now.TimeOfDay > _serversWipeScheduler.WipeTime)
            {
                daysUntilTarget = 7;
            }

            var nextExecution = now.Date.AddDays(daysUntilTarget).Add(_serversWipeScheduler.WipeTime);

            var timeUntil = nextExecution - now;

            remainingTime.Days = timeUntil.Days;
            remainingTime.Hours = timeUntil.Hours;
            remainingTime.Minutes = timeUntil.Minutes;

            return Ok(remainingTime);
        }
    }
}
