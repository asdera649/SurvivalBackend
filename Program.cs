using SurvivalBackend.Jobs;
using Quartz;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

builder.Services.AddHttpClient();

builder.Services.AddQuartz(q =>
{

});

builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

builder.Services.AddTransient<ServersWipeHandler>();

builder.Services.AddSingleton<ServersWipeScheduler>();

builder.Services.AddControllers();

var app = builder.Build();

var scheduler = app.Services.GetRequiredService<ServersWipeScheduler>();

await scheduler.Start();

app.UseAuthorization();
app.MapControllers();
app.Run();