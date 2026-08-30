namespace SurvivalBackend.Services;

public interface IServersWipeService
{
    WipeOperationState CurrentState { get; }
    Task RunAsync(string triggeredBy, CancellationToken cancellationToken);
    bool TryStartInBackground(string triggeredBy);
}
