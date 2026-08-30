namespace SurvivalBackend.Infrastructure.Edgegap;

public interface IEdgegapClient
{
    Task<IReadOnlyList<EdgegapDeployment>> GetDeploymentsAsync(CancellationToken cancellationToken);
    Task<EdgegapDeploymentStatus> GetDeploymentStatusAsync(string requestId, CancellationToken cancellationToken);
    Task<IReadOnlyList<EdgegapFleet>> GetFleetsAsync(CancellationToken cancellationToken);
    Task SetFleetEnabledAsync(string fleetName, bool enabled, CancellationToken cancellationToken);
    Task BulkStopDeploymentsAsync(CancellationToken cancellationToken);
}
