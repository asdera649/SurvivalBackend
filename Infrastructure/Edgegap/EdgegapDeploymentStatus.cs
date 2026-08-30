namespace SurvivalBackend.Infrastructure.Edgegap;

public sealed record EdgegapDeploymentStatus(
    string? CurrentStatus,
    bool Running,
    string? PublicIp,
    int? GamePortExternal);
