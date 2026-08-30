namespace SurvivalBackend.Options;

public sealed class EdgegapOptions
{
    public const string SectionName = "Edgegap";

    public string BaseUrl { get; set; } = "https://api.edgegap.com/v1/";
    public string Token { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 15;
    public int RetryAttempts { get; set; } = 3;
    public int RetryBaseDelayMs { get; set; } = 500;
}
