namespace SurvivalBackend.Options;

public sealed class WipeOptions
{
    public const string SectionName = "Wipe";

    public string DayOfWeek { get; set; } = "Monday";
    public string Time { get; set; } = "10:50";
    public string TimeZone { get; set; } = "Europe/Moscow";
}
