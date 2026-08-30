using Microsoft.Extensions.Options;
using SurvivalBackend.Options;

namespace SurvivalBackend.Services;

public sealed class GameClientVersionProvider(IOptionsMonitor<GameClientOptions> options) : IGameClientVersionProvider
{
    private readonly IOptionsMonitor<GameClientOptions> _options = options;

    public string CurrentVersion => _options.CurrentValue.CurrentVersion;
}
