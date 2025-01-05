using SurvivalBackend.Jobs;
using Quartz;
using SurvivalBackend.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();

builder.Logging.AddConsole();

builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

builder.Services.AddHttpClient();

builder.Services.AddQuartz(q =>
{

});

builder.Services.AddLogging();

builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

builder.Services.AddSingleton<ServersListService>();

builder.Services.AddTransient<ServersWipeHandler>();

builder.Services.AddSingleton<ServersWipeScheduler>();

builder.Services.AddControllers();

var app = builder.Build();

var scheduler = app.Services.GetRequiredService<ServersWipeScheduler>();

await scheduler.Start();

var serversListService = app.Services.GetRequiredService<ServersListService>();

await serversListService.Load();

app.MapGet("/", () => "Survival Backend");

app.UseAuthorization();
app.MapControllers();
app.Run();