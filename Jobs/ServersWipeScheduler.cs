using Quartz;
using Newtonsoft.Json;

namespace SurvivalBackend.Jobs
{
    public class ServersWipeScheduler
    {
        public class WipeSettings
        {
            public required string DayOfWeek { get; set; }
            public required string Time { get; set; }
        }

        public ServersWipeScheduler(ISchedulerFactory schedulerFactory)
        {
            _schedulerFactory = schedulerFactory;
        }

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

        private TimeSpan _wipeTime;
        public TimeSpan WipeTime
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

        private readonly ISchedulerFactory _schedulerFactory;

        public async Task Start()
        {
            if (_isStarted)
            {
                throw new Exception("ServersWipeScheduler is already running!");
            }

            _isStarted = true;

            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wipesettings.json");
            var jsonData = File.ReadAllText(filePath);

            var wipeSettings = JsonConvert.DeserializeObject<WipeSettings>(jsonData);

            if (wipeSettings == null)
            {
                throw new Exception("Failed to desserialize WipeSettings!");
            }

            _wipeDayOfWeek = Enum.Parse<DayOfWeek>(wipeSettings.DayOfWeek, ignoreCase: true);
            _wipeTime = TimeSpan.Parse(wipeSettings.Time);

            var cronExpression = CronExpressionForDayAndTime(_wipeDayOfWeek, _wipeTime);

            IScheduler scheduler = await _schedulerFactory.GetScheduler();

            await scheduler.Start();

            IJobDetail job = JobBuilder.Create<ServersWipeHandler>().Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("trigger1", "group1")
                .WithCronSchedule(cronExpression)
                .Build();

            await scheduler.ScheduleJob(job, trigger);
        }

        private string CronExpressionForDayAndTime(DayOfWeek dayOfWeek, TimeSpan time)
        {
            var day = dayOfWeek.ToString().Substring(0, 3).ToUpper();

            return $"0 {time.Minutes} {time.Hours} ? * {day} *";
        }
    }
}
