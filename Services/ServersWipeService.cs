using SurvivalBackend.Infrastructure.Edgegap;
using SurvivalBackend.Infrastructure.Storage;

namespace SurvivalBackend.Services;

public sealed class ServersWipeService(
    IEdgegapClient edgegapClient,
    ISaveStorage saveStorage,
    ServersListService serversListService,
    IHostApplicationLifetime applicationLifetime,
    ILogger<ServersWipeService> logger) : IServersWipeService
{
    private readonly IEdgegapClient _edgegapClient = edgegapClient;
    private readonly ISaveStorage _saveStorage = saveStorage;
    private readonly ServersListService _serversListService = serversListService;
    private readonly IHostApplicationLifetime _applicationLifetime = applicationLifetime;
    private readonly ILogger<ServersWipeService> _logger = logger;
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private readonly object _stateLock = new();

    private WipeOperationState _currentState = WipeOperationState.Idle;

    public WipeOperationState CurrentState
    {
        get
        {
            lock (_stateLock)
            {
                return _currentState;
            }
        }
    }

    public async Task RunAsync(string triggeredBy, CancellationToken cancellationToken)
    {
        await _runLock.WaitAsync(cancellationToken);
        try
        {
            await RunCoreAsync(triggeredBy, cancellationToken);
        }
        finally
        {
            _runLock.Release();
        }
    }

    public bool TryStartInBackground(string triggeredBy)
    {
        if (!_runLock.Wait(0))
        {
            return false;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await RunCoreAsync(triggeredBy, _applicationLifetime.ApplicationStopping);
            }
            catch (Exception)
            {
                // RunCoreAsync already records the failure in CurrentState and logs the exception.
            }
            finally
            {
                _runLock.Release();
            }
        }, CancellationToken.None);

        return true;
    }

    private async Task RunCoreAsync(string triggeredBy, CancellationToken cancellationToken)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        SetState(new WipeOperationState("Running", triggeredBy, "Starting", startedAtUtc, null, null));

        try
        {
            _logger.LogInformation("Wipe started by {TriggeredBy}.", triggeredBy);

            await SetStepAndRunAsync("Disabling Edgegap fleets", DisableEdgegapFleetsAsync, cancellationToken);
            await SetStepAndRunAsync("Stopping Edgegap deployments", StopEdgegapDeploymentsAsync, cancellationToken);
            await SetStepAndRunAsync("Clearing current wipe saves", _saveStorage.ClearCurrentWipeSavesAsync, cancellationToken);
            await SetStepAndRunAsync("Clearing server registry", _serversListService.ClearAsync, cancellationToken);
            await SetStepAndRunAsync("Enabling Edgegap fleets", EnableEdgegapFleetsAsync, cancellationToken);

            SetState(new WipeOperationState("Succeeded", triggeredBy, null, startedAtUtc, DateTimeOffset.UtcNow, null));
            _logger.LogInformation("Wipe completed successfully.");
        }
        catch (Exception exception)
        {
            SetState(new WipeOperationState(
                "Failed",
                triggeredBy,
                null,
                startedAtUtc,
                DateTimeOffset.UtcNow,
                exception.Message));

            _logger.LogError(exception, "Wipe failed.");
            throw;
        }
    }

    private async Task SetStepAndRunAsync(
        string step,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        var previous = CurrentState;
        SetState(previous with { CurrentStep = step });
        _logger.LogInformation("Wipe step: {Step}.", step);
        await operation(cancellationToken);
    }

    private async Task DisableEdgegapFleetsAsync(CancellationToken cancellationToken)
    {
        var fleets = await _edgegapClient.GetFleetsAsync(cancellationToken);
        foreach (var fleet in fleets.Where(fleet => fleet.Enabled))
        {
            await _edgegapClient.SetFleetEnabledAsync(fleet.Name, enabled: false, cancellationToken);
        }
    }

    private Task StopEdgegapDeploymentsAsync(CancellationToken cancellationToken)
    {
        return _edgegapClient.BulkStopDeploymentsAsync(cancellationToken);
    }

    private async Task EnableEdgegapFleetsAsync(CancellationToken cancellationToken)
    {
        var fleets = await _edgegapClient.GetFleetsAsync(cancellationToken);
        foreach (var fleet in fleets.Where(fleet => !fleet.Enabled))
        {
            await _edgegapClient.SetFleetEnabledAsync(fleet.Name, enabled: true, cancellationToken);
        }
    }

    private void SetState(WipeOperationState state)
    {
        lock (_stateLock)
        {
            _currentState = state;
        }
    }
}
