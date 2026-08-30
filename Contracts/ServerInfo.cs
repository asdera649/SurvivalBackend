namespace SurvivalBackend.Contracts;

public sealed class ServerInfo
{
    public required string Ip { get; init; }
    public required string UniqueId { get; init; }
    public required string Name { get; init; }
    public int MaxPlayersCount { get; init; }
    public int CurrentPlayersCount { get; init; }
}
