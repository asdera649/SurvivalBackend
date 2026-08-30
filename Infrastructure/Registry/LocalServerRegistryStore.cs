using System.Text.Json;
using Microsoft.Extensions.Options;
using SurvivalBackend.Contracts;
using SurvivalBackend.Options;

namespace SurvivalBackend.Infrastructure.Registry;

public sealed class LocalServerRegistryStore(
    IOptions<ServerRegistryOptions> options,
    ILogger<LocalServerRegistryStore> logger) : IServerRegistryStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ServerRegistryOptions _options = options.Value;
    private readonly ILogger<LocalServerRegistryStore> _logger = logger;

    public async Task<IReadOnlyList<ServerContainer>> LoadAsync(CancellationToken cancellationToken)
    {
        var path = ResolvePath(_options.LocalPath);
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);
        var servers = await JsonSerializer.DeserializeAsync<List<ServerContainer>>(stream, SerializerOptions, cancellationToken);
        return servers ?? [];
    }

    public async Task SaveAsync(IReadOnlyList<ServerContainer> servers, CancellationToken cancellationToken)
    {
        var path = ResolvePath(_options.LocalPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppContext.BaseDirectory);

        var tempPath = path + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, servers, SerializerOptions, cancellationToken);
        }

        File.Move(tempPath, path, overwrite: true);
        _logger.LogDebug("Saved {Count} server registry records to {Path}.", servers.Count, path);
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        var path = ResolvePath(_options.LocalPath);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private static string ResolvePath(string path)
    {
        return Path.IsPathRooted(path)
            ? path
            : Path.Combine(AppContext.BaseDirectory, path);
    }
}
