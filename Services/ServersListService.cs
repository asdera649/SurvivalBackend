using Microsoft.Extensions.Options;
using SurvivalBackend.Contracts;
using SurvivalBackend.Infrastructure.Registry;
using SurvivalBackend.Options;

namespace SurvivalBackend.Services;

public sealed class ServersListService(
    IServerRegistryStore registryStore,
    IOptions<ServerRegistryOptions> registryOptions,
    ILogger<ServersListService> logger)
{
    public const string UnassignedRequestId = "null";

    private static readonly IReadOnlyList<string> PossibleServerNames =
    [
        "#1 Server",
        "#2 Server",
        "#3 Server",
        "#4 Server",
        "#5 Server",
        "#6 Server",
        "#7 Server",
        "#8 Server",
        "#9 Server",
        "#10 Server",
        "#11 Server",
        "#12 Server",
        "#13 Server",
        "#14 Server",
        "#15 Server"
    ];

    private readonly IServerRegistryStore _registryStore = registryStore;
    private readonly ServerRegistryOptions _registryOptions = registryOptions.Value;
    private readonly ILogger<ServersListService> _logger = logger;
    private readonly SemaphoreSlim _mutationLock = new(1, 1);
    private readonly object _sync = new();
    private readonly List<ServerContainer> _items = [];
    private readonly Dictionary<string, ServerRuntimeState> _runtimeStates = [];

    private bool _isLoaded;

    public IReadOnlyList<ServerContainer> Items => GetServersSnapshot();

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Loading server registry...");
            var loadedServers = await _registryStore.LoadAsync(cancellationToken);
            var normalizedServers = NormalizeLoadedServers(loadedServers);

            ReplaceItems(normalizedServers, isLoaded: true);
            _logger.LogInformation("Loaded {Count} server registry records.", normalizedServers.Count);
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async Task<ServerContainer> RegisterDeploymentAsync(
        string requestId,
        IReadOnlySet<string> activeRequestIds,
        CancellationToken cancellationToken)
    {
        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            EnsureLoaded();

            var candidate = GetServersSnapshotNoThrow().ToList();
            var releasedCount = ReleaseMissingDeployments(candidate, activeRequestIds);

            var existingIndex = candidate.FindIndex(server => server.RequestId == requestId);
            if (existingIndex >= 0)
            {
                if (releasedCount > 0)
                {
                    await PersistAndCommitAsync(candidate, cancellationToken);
                }

                return candidate[existingIndex];
            }

            var freeIndex = candidate.FindIndex(server => server.RequestId == UnassignedRequestId);
            if (freeIndex >= 0)
            {
                var freeServer = candidate[freeIndex];
                candidate[freeIndex] = freeServer with
                {
                    RequestId = requestId,
                    Ready = false
                };

                await PersistAndCommitAsync(candidate, cancellationToken);
                return candidate[freeIndex];
            }

            var newServer = new ServerContainer(
                Guid.NewGuid().ToString("D"),
                GetNewName(candidate),
                requestId,
                Ready: false);

            candidate.Add(newServer);
            await PersistAndCommitAsync(candidate, cancellationToken);
            return newServer;
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async Task<bool> MarkReadyAsync(string requestId, CancellationToken cancellationToken)
    {
        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            EnsureLoaded();

            var candidate = GetServersSnapshotNoThrow().ToList();
            var index = candidate.FindIndex(server => server.RequestId == requestId);
            if (index < 0)
            {
                return false;
            }

            if (candidate[index].Ready)
            {
                return true;
            }

            candidate[index] = candidate[index] with { Ready = true };
            await PersistAndCommitAsync(candidate, cancellationToken);
            return true;
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async Task<int> ReleaseMissingDeploymentsAsync(
        IReadOnlySet<string> activeRequestIds,
        CancellationToken cancellationToken)
    {
        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            EnsureLoaded();

            var candidate = GetServersSnapshotNoThrow().ToList();
            var releasedCount = ReleaseMissingDeployments(candidate, activeRequestIds);
            if (releasedCount == 0)
            {
                return 0;
            }

            await PersistAndCommitAsync(candidate, cancellationToken);
            return releasedCount;
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            EnsureLoaded();

            await _registryStore.ClearAsync(cancellationToken);
            ReplaceItems([], isLoaded: true);

            lock (_sync)
            {
                _runtimeStates.Clear();
            }
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public void UpdateRuntimeState(string requestId, ServerState serverState)
    {
        if (serverState.CurrentPlayersCount > serverState.MaxPlayersCount)
        {
            throw new ArgumentException("CurrentPlayersCount cannot be greater than MaxPlayersCount.");
        }

        lock (_sync)
        {
            _runtimeStates[requestId] = new ServerRuntimeState(
                serverState.MaxPlayersCount,
                serverState.CurrentPlayersCount,
                DateTimeOffset.UtcNow);
        }
    }

    public bool TryGetRuntimeState(string requestId, out ServerRuntimeState runtimeState)
    {
        lock (_sync)
        {
            RemoveStaleRuntimeStatesNoLock();
            return _runtimeStates.TryGetValue(requestId, out runtimeState!);
        }
    }

    public IReadOnlyDictionary<string, ServerRuntimeState> GetRuntimeStatesSnapshot()
    {
        lock (_sync)
        {
            RemoveStaleRuntimeStatesNoLock();
            return new Dictionary<string, ServerRuntimeState>(_runtimeStates);
        }
    }

    public IReadOnlyList<ServerContainer> GetServersSnapshot()
    {
        EnsureLoaded();
        return GetServersSnapshotNoThrow();
    }

    private IReadOnlyList<ServerContainer> GetServersSnapshotNoThrow()
    {
        lock (_sync)
        {
            return _items.ToList();
        }
    }

    private async Task PersistAndCommitAsync(
        IReadOnlyList<ServerContainer> candidate,
        CancellationToken cancellationToken)
    {
        await _registryStore.SaveAsync(candidate, cancellationToken);
        ReplaceItems(candidate, isLoaded: true);
    }

    private void ReplaceItems(IReadOnlyList<ServerContainer> items, bool isLoaded)
    {
        lock (_sync)
        {
            _items.Clear();
            _items.AddRange(items);
            _isLoaded = isLoaded;
        }
    }

    private void EnsureLoaded()
    {
        lock (_sync)
        {
            if (!_isLoaded)
            {
                throw new InvalidOperationException("Server registry has not been loaded yet.");
            }
        }
    }

    private int ReleaseMissingDeployments(
        List<ServerContainer> servers,
        IReadOnlySet<string> activeRequestIds)
    {
        var releasedCount = 0;

        for (var index = 0; index < servers.Count; index++)
        {
            var server = servers[index];
            if (server.RequestId == UnassignedRequestId || activeRequestIds.Contains(server.RequestId))
            {
                continue;
            }

            servers[index] = server with
            {
                RequestId = UnassignedRequestId,
                Ready = false
            };

            releasedCount++;
        }

        if (releasedCount > 0)
        {
            _logger.LogInformation("Released {Count} inactive server registry slots.", releasedCount);
        }

        return releasedCount;
    }

    private IReadOnlyList<ServerContainer> NormalizeLoadedServers(IReadOnlyList<ServerContainer> loadedServers)
    {
        var normalized = new List<ServerContainer>();
        var uniqueIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var server in loadedServers)
        {
            if (string.IsNullOrWhiteSpace(server.UniqueId)
                || string.IsNullOrWhiteSpace(server.ServerName)
                || string.IsNullOrWhiteSpace(server.RequestId))
            {
                _logger.LogWarning("Skipped malformed server registry record {@Server}.", server);
                continue;
            }

            if (!uniqueIds.Add(server.UniqueId))
            {
                _logger.LogWarning("Skipped duplicate server registry record with UniqueId {UniqueId}.", server.UniqueId);
                continue;
            }

            normalized.Add(server);
        }

        return normalized;
    }

    private static string GetNewName(IReadOnlyList<ServerContainer> servers)
    {
        foreach (var name in PossibleServerNames)
        {
            if (servers.All(server => server.ServerName != name))
            {
                return name;
            }
        }

        return $"Server({Guid.NewGuid():D})";
    }

    private void RemoveStaleRuntimeStatesNoLock()
    {
        var now = DateTimeOffset.UtcNow;
        var maxAge = TimeSpan.FromSeconds(_registryOptions.StaleServerStateSeconds);

        foreach (var key in _runtimeStates
                     .Where(item => now - item.Value.UpdatedAtUtc > maxAge)
                     .Select(item => item.Key)
                     .ToList())
        {
            _runtimeStates.Remove(key);
        }
    }
}