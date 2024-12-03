using Quartz;

namespace SurvivalBackend.Jobs
{
    public class ServersWipeHandler : IJob
    {
        public ServersWipeHandler(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        private readonly HttpClient _httpClient;

        public async Task Execute(IJobExecutionContext context)
        {
            await Task.Delay(5000);

            if (_httpClient != null)
            {
                Console.WriteLine("notNull");
            }

            Console.WriteLine("Execute");
        }
    }
}
