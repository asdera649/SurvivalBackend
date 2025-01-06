using Microsoft.AspNetCore.Mvc;
using NodaTime;
using SurvivalBackend.Jobs;

namespace SurvivalBackend.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ServersWipeController(ServersWipeScheduler serversWipeScheduler) : ControllerBase
    {
        #region Structs

        public class RemainingTimeToWipe
        {
            public int Days { get; set; }
            public int Hours { get; set; }
            public int Minutes { get; set; }
        }

        #endregion

        private readonly ServersWipeScheduler _serversWipeScheduler = serversWipeScheduler;

        [HttpGet("remainingTimeToWipe")]
        public IActionResult GetRemainingTimeToWipe()
        {
            var remainingTime = new RemainingTimeToWipe();

            var now = SystemClock.Instance.GetCurrentInstant().InZone(DateTimeZoneProviders.Tzdb.GetSystemDefault());

            var nextExecution = GetNextExecutionDate();

            var timeUntil = nextExecution - now;

            remainingTime.Days = timeUntil.Days;
            remainingTime.Hours = timeUntil.Hours;
            remainingTime.Minutes = timeUntil.Minutes;

            return Ok(remainingTime);
        }

        public ZonedDateTime GetNextExecutionDate()
        {
            var now = SystemClock.Instance.GetCurrentInstant().InZone(DateTimeZoneProviders.Tzdb.GetSystemDefault());

            int daysUntilTarget = ((int)_serversWipeScheduler.WipeDayOfWeek - (int)now.DayOfWeek + 7) % 7;

            if (daysUntilTarget == 0 && now.TimeOfDay > _serversWipeScheduler.WipeTime)
            {
                daysUntilTarget = 7;
            }

            LocalDate nextExecutionDate = now.Date.PlusDays(daysUntilTarget);
            ZonedDateTime nextExecutionDateTime = nextExecutionDate.At(_serversWipeScheduler.WipeTime).InZoneStrictly(now.Zone);

            return nextExecutionDateTime;
        }

        //public DateTime GetNextExecutionDate()
        //{
        //    var now = DateTime.Now;

        //    int daysUntilTarget = ((int)_serversWipeScheduler.WipeDayOfWeek - (int)now.DayOfWeek + 7) % 7;

        //    if (daysUntilTarget == 0 && now.TimeOfDay > _serversWipeScheduler.WipeTime)
        //    {
        //        daysUntilTarget = 7;
        //    }

        //    return now.Date.AddDays(daysUntilTarget).Add(_serversWipeScheduler.WipeTime);
        //}
    }
}
