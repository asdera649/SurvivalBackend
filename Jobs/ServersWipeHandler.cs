using Quartz;
using SurvivalBackend.Services;

namespace SurvivalBackend.Jobs;

[DisallowConcurrentExecution]
public sealed class ServersWipeHandler(IServersWipeService serversWipeService) : IJob
{
    private readonly IServersWipeService _serversWipeService = serversWipeService;

    public Task Execute(IJobExecutionContext context)
    {
        return _serversWipeService.RunAsync("scheduler", context.CancellationToken);
    }
}
