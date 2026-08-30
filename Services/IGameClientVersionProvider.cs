namespace SurvivalBackend.Services;

public interface IGameClientVersionProvider
{
    string CurrentVersion { get; }
}
