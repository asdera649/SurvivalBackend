using Microsoft.Extensions.Options;
using NodaTime;
using Quartz;
using SurvivalBackend.Options;

namespace SurvivalBackend.Jobs;

public sealed class ServersWipeScheduler(
    ISchedulerFactory schedulerFactory,
    IOptions<WipeOptions> options,
    ILogger<ServersWipeScheduler> logger)
{
    private readonly ISchedulerFactory _schedulerFactory = schedulerFactory;
    private readonly WipeOptions _options = options.Value;
    private readonly ILogger<ServersWipeScheduler> _logger = logger;

    private bool _isStarted;
    private IsoDayOfWeek _wipeDayOfWeek;
    private LocalTime _wipeTime;
    private DateTimeZone _wipeZone = DateTimeZoneProviders.Tzdb.GetSystemDefault();

    public IsoDayOfWeek WipeDayOfWeek
    {
        get
        {
            EnsureStarted();
            return _wipeDayOfWeek;
        }
    }

    public LocalTime WipeTime
    {
        get
        {
            EnsureStarted();
            return _wipeTime;
        }
    }

    public string WipeTimeZone
    {
        get
        {
            EnsureStarted();
            return _wipeZone.Id;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isStarted)
        {
            throw new InvalidOperationException("ServersWipeScheduler is already running.");
        }

        _wipeDayOfWeek = Enum.Parse<IsoDayOfWeek>(_options.DayOfWeek, ignoreCase: true);
        _wipeTime = ParseLocalTime(_options.Time);
        _wipeZone = DateTimeZoneProviders.Tzdb[_options.TimeZone];

        var scheduleDay = _wipeDayOfWeek;
        var scheduleTime = _wipeTime;
        var cronExpression = CronExpressionForDayAndTime(scheduleDay, scheduleTime);

        IScheduler scheduler = await _schedulerFactory.GetScheduler(cancellationToken);
        await scheduler.Start(cancellationToken);

        IJobDetail job = JobBuilder.Create<ServersWipeHandler>()
            .WithIdentity("servers-wipe", "survival")
            .Build();

        var scheduleBuilder = CronScheduleBuilder.CronSchedule(cronExpression);
        var quartzTimeZone = TryFindTimeZoneInfo(_wipeZone.Id);
        if (quartzTimeZone is not null)
        {
            scheduleBuilder = scheduleBuilder.InTimeZone(quartzTimeZone);
        }
        else
        {
            var localNextExecution = CalculateNextExecutionDate()
                .WithZone(DateTimeZoneProviders.Tzdb.GetSystemDefault());

            scheduleDay = localNextExecution.DayOfWeek;
            scheduleTime = localNextExecution.TimeOfDay;
            cronExpression = CronExpressionForDayAndTime(scheduleDay, scheduleTime);
            scheduleBuilder = CronScheduleBuilder.CronSchedule(cronExpression);

            _logger.LogWarning(
                "Time zone {TimeZone} is not supported by TimeZoneInfo on this host. Wipe cron was converted to local {Day} {Time}.",
                _wipeZone.Id,
                scheduleDay,
                scheduleTime);
        }

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("servers-wipe-trigger", "survival")
            .WithSchedule(scheduleBuilder)
            .Build();

        await scheduler.ScheduleJob(job, trigger, cancellationToken);

        _isStarted = true;
        _logger.LogInformation(
            "Servers wipe scheduler started for {DayOfWeek} {Time} {TimeZone}.",
            _wipeDayOfWeek,
            _wipeTime,
            _wipeZone.Id);
    }

    public ZonedDateTime GetNextExecutionDate()
    {
        EnsureStarted();
        return CalculateNextExecutionDate();
    }

    private ZonedDateTime CalculateNextExecutionDate()
    {
        var now = SystemClock.Instance.GetCurrentInstant().InZone(_wipeZone);
        var daysUntilTarget = ((int)_wipeDayOfWeek - (int)now.DayOfWeek + 7) % 7;

        if (daysUntilTarget == 0 && now.TimeOfDay > _wipeTime)
        {
            daysUntilTarget = 7;
        }

        var nextExecutionDate = now.Date.PlusDays(daysUntilTarget);
        return nextExecutionDate.At(_wipeTime).InZoneLeniently(_wipeZone);
    }

    private void EnsureStarted()
    {
        if (!_isStarted)
        {
            throw new InvalidOperationException("ServersWipeScheduler is not running.");
        }
    }

    private static LocalTime ParseLocalTime(string value)
    {
        var parts = value.Split(':', StringSplitOptions.TrimEntries);
        return new LocalTime(int.Parse(parts[0]), int.Parse(parts[1]));
    }

    private static string CronExpressionForDayAndTime(IsoDayOfWeek dayOfWeek, LocalTime time)
    {
        return $"0 {time.Minute} {time.Hour} ? * {ToQuartzDay(dayOfWeek)} *";
    }

    private static string ToQuartzDay(IsoDayOfWeek dayOfWeek)
    {
        return dayOfWeek switch
        {
            IsoDayOfWeek.Monday => "MON",
            IsoDayOfWeek.Tuesday => "TUE",
            IsoDayOfWeek.Wednesday => "WED",
            IsoDayOfWeek.Thursday => "THU",
            IsoDayOfWeek.Friday => "FRI",
            IsoDayOfWeek.Saturday => "SAT",
            IsoDayOfWeek.Sunday => "SUN",
            _ => throw new ArgumentOutOfRangeException(nameof(dayOfWeek), dayOfWeek, null)
        };
    }

    private static TimeZoneInfo? TryFindTimeZoneInfo(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
    }
}
