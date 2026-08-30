namespace SurvivalBackend.Options;

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    public bool RequireApiKeys { get; set; } = true;
    public string ServerApiKey { get; set; } = string.Empty;
    public string AdminApiKey { get; set; } = string.Empty;
    public string ServerApiKeyHeaderName { get; set; } = "X-Server-Api-Key";
    public string AdminApiKeyHeaderName { get; set; } = "X-Admin-Api-Key";
}
