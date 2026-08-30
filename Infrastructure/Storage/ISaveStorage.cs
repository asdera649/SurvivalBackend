using SurvivalBackend.Contracts;

namespace SurvivalBackend.Infrastructure.Storage;

public interface ISaveStorage
{
    ServerRegistrationData CreateServerSaveAccess(string serverName);
    Task ClearCurrentWipeSavesAsync(CancellationToken cancellationToken);
}
