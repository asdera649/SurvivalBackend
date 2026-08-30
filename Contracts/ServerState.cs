using System.ComponentModel.DataAnnotations;

namespace SurvivalBackend.Contracts;

public sealed class ServerState
{
    [Range(1, 10000)]
    public int MaxPlayersCount { get; set; }

    [Range(0, 10000)]
    public int CurrentPlayersCount { get; set; }
}
