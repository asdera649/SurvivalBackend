using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Quartz;
using SurvivalBackend.Admin;
using SurvivalBackend.Infrastructure.Edgegap;
using SurvivalBackend.Infrastructure.Registry;
using SurvivalBackend.Infrastructure.Storage;
using SurvivalBackend.Jobs;
using SurvivalBackend.Options;
using SurvivalBackend.Security;
using SurvivalBackend.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddProblemDetails();
builder.Services.AddControllers();
builder.Services.AddHealthChecks();
builder.Services.AddSurvivalOptions(builder.Configuration, builder.Environment);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter(RateLimitPolicies.Public, limiter =>
    {
        limiter.PermitLimit = 180;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });

    options.AddFixedWindowLimiter(RateLimitPolicies.Management, limiter =>
    {
        limiter.PermitLimit = 90;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });

    options.AddFixedWindowLimiter(RateLimitPolicies.Admin, limiter =>
    {
        limiter.PermitLimit = 60;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

builder.Services.AddHttpClient(EdgegapClient.HttpClientName, (serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<EdgegapOptions>>().Value;
    client.BaseAddress = new Uri(EnsureTrailingSlash(options.BaseUrl));
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});

builder.Services.AddSingleton<IEdgegapClient, EdgegapClient>();
builder.Services.AddSingleton<ISaveStorage, S3SaveStorage>();
builder.Services.AddSingleton<LocalServerRegistryStore>();
builder.Services.AddSingleton<S3ServerRegistryStore>();
builder.Services.AddSingleton<IServerRegistryStore>(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<ServerRegistryOptions>>().Value;
    return string.Equals(options.StorageMode, "LocalFile", StringComparison.OrdinalIgnoreCase)
        ? serviceProvider.GetRequiredService<LocalServerRegistryStore>()
        : serviceProvider.GetRequiredService<S3ServerRegistryStore>();
});
builder.Services.AddSingleton<ServersListService>();
builder.Services.AddSingleton<IGameClientVersionProvider, GameClientVersionProvider>();
builder.Services.AddSingleton<IServersWipeService, ServersWipeService>();

builder.Services.AddQuartz();
builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
builder.Services.AddTransient<ServersWipeHandler>();
builder.Services.AddSingleton<ServersWipeScheduler>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseForwardedHeaders();
app.UseRateLimiter();
app.UseAuthorization();

var serversListService = app.Services.GetRequiredService<ServersListService>();
await serversListService.LoadAsync(app.Lifetime.ApplicationStopping);

var scheduler = app.Services.GetRequiredService<ServersWipeScheduler>();
await scheduler.StartAsync(app.Lifetime.ApplicationStopping);

app.MapGet("/", () => Results.Ok(new
{
    Service = "Survival Backend",
    Status = "Ok",
    Environment = app.Environment.EnvironmentName
}));

app.MapHealthChecks("/health");
app.MapGet("/ready", (ServersListService registry) =>
{
    try
    {
        return Results.Ok(new
        {
            Status = "Ready",
            ServerCount = registry.GetServersSnapshot().Count
        });
    }
    catch (InvalidOperationException exception)
    {
        return Results.Problem(
            title: "Backend is not ready.",
            detail: exception.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/admin", () => Results.Content(AdminPanel.Html, "text/html; charset=utf-8"));
app.MapControllers();

app.Run();

static string EnsureTrailingSlash(string value)
{
    return value.EndsWith('/') ? value : value + "/";
}
