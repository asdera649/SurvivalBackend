namespace SurvivalBackend.Infrastructure.Edgegap;

public sealed record EdgegapDeployment(
    string RequestId,
    string PublicIp,
    bool Ready);
