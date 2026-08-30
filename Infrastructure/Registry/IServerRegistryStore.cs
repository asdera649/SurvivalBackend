using SurvivalBackend.Contracts;

namespace SurvivalBackend.Infrastructure.Registry;

public interface IServerRegistryStore
{
    Task<IReadOnlyList<ServerContainer>> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(IReadOnlyList<ServerContainer> servers, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}
