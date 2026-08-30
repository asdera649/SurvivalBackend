namespace SurvivalBackend.Contracts;

public sealed record ServerRuntimeState(
    int MaxPlayersCount,
    int CurrentPlayersCount,
    DateTimeOffset UpdatedAtUtc);
