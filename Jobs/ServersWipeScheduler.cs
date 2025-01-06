using Quartz;
using Newtonsoft.Json;
using NodaTime;

namespace SurvivalBackend.Jobs
{
    public class ServersWipeScheduler(ISchedulerFactory schedulerFactory, ILogger<ServersWipeScheduler> logger)
    {
        #region Structs

        public class WipeSettings
        {
            public required string DayOfWeek { get; set; }
            public required string Time { get; set; }
            public required string TimeZone { get; set; }
        }

        #endregion

        private bool _isStarted;

        private DayOfWeek _wipeDayOfWeek;
        public DayOfWeek WipeDayOfWeek
        {
            get
            {
                if (!_isStarted)
                {
                    throw new Exception("ServersWipeScheduler is not running!");
                }

                return _wipeDayOfWeek;
            }
        }

        private LocalTime _wipeTime;
        public LocalTime WipeTime
        {
            get
            {
                if (!_isStarted)
                {
                    throw new Exception("ServersWipeScheduler is not running!");
                }

                return _wipeTime;
            }
        }

        private readonly ISchedulerFactory _schedulerFactory = schedulerFactory;
        private readonly ILogger<ServersWipeScheduler> _logger = logger;

        public async Task Start()
        {
            if (_isStarted)
            {
                throw new Exception("ServersWipeScheduler is already running!");
            }

            _isStarted = true;

            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wipesettings.json");
            var jsonData = File.ReadAllText(filePath);

            var wipeSettings = JsonConvert.DeserializeObject<WipeSettings>(jsonData) ?? throw new Exception("Failed to desserialize WipeSettings!");

            _wipeDayOfWeek = Enum.Parse<DayOfWeek>(wipeSettings.DayOfWeek, ignoreCase: true);

            DateTimeZone targetZone = DateTimeZoneProviders.Tzdb[wipeSettings.TimeZone];

            LocalTime targetTime = LocalTime.FromHourMinuteSecondNanosecond(
                int.Parse(wipeSettings.Time.Split(':')[0]),
                int.Parse(wipeSettings.Time.Split(':')[1]),
                0, 0);

            LocalDate today = SystemClock.Instance.GetCurrentInstant().InZone(DateTimeZoneProviders.Tzdb.GetSystemDefault()).Date;

            ZonedDateTime targetDateTime = targetZone.AtStrictly(today.At(targetTime));

            ZonedDateTime localDateTime = targetDateTime.WithZone(DateTimeZoneProviders.Tzdb.GetSystemDefault());

            _wipeTime = localDateTime.TimeOfDay;

            var cronExpression = CronExpressionForDayAndTime(_wipeDayOfWeek, _wipeTime);

            IScheduler scheduler = await _schedulerFactory.GetScheduler();

            await scheduler.Start();

            IJobDetail job = JobBuilder.Create<ServersWipeHandler>().Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("trigger1", "group1")
                .WithCronSchedule(cronExpression)
                .Build();

            await scheduler.ScheduleJob(job, trigger);

            _logger.LogInformation("[ServersWipeScheduler] Started a wiping task on: " + _wipeDayOfWeek + ", " + _wipeTime);
        }

        private static string CronExpressionForDayAndTime(DayOfWeek dayOfWeek, LocalTime time)
        {
            var day = dayOfWeek.ToString()[..3].ToUpper();

            return $"0 {time.Minute} {time.Hour} ? * {day} *";
        }
    }
}
