using SurvivalBackend;

var builder = WebApplication.CreateBuilder(args);

// Добавление конфигурации из appsettings.json
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

builder.Services.AddControllers();

var app = builder.Build();

app.UseAuthorization();
app.MapControllers();
app.Run();