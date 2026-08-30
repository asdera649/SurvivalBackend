namespace SurvivalBackend.Options;

public sealed class ServerRegistryOptions
{
    public const string SectionName = "ServerRegistry";

    public string StorageMode { get; set; } = "S3";
    public string LocalPath { get; set; } = "Data/server-registry.json";
    public string LocalCachePath { get; set; } = "Data/ServersListSaves";
    public int StaleServerStateSeconds { get; set; } = 90;
}
