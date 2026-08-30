namespace SurvivalBackend.Contracts;

public sealed record ServerContainer(
    string UniqueId,
    string ServerName,
    string RequestId,
    bool Ready);
