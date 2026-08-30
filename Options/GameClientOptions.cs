namespace SurvivalBackend.Options;

public sealed class GameClientOptions
{
    public const string SectionName = "GameClient";

    public string CurrentVersion { get; set; } = "0.0.1";
}
