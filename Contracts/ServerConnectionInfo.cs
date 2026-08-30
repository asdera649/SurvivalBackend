namespace SurvivalBackend.Contracts;

public sealed class ServerConnectionInfo
{
    public required string PublicIp { get; init; }
    public int ExternalPort { get; init; }
}
