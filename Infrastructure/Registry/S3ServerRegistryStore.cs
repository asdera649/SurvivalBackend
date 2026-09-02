using System.Net;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using SurvivalBackend.Contracts;
using SurvivalBackend.Options;
using SurvivalBackend.Infrastructure.Storage;

namespace SurvivalBackend.Infrastructure.Registry;

public sealed class S3ServerRegistryStore(
    IOptions<S3Options> options,
    ILogger<S3ServerRegistryStore> logger) : IServerRegistryStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly S3Options _options = options.Value;
    private readonly ILogger<S3ServerRegistryStore> _logger = logger;

    private string RegistryObjectKey => S3SaveStorage.CombineS3Key(_options.ServersListSavesPath, "registry.json");

    public async Task<IReadOnlyList<ServerContainer>> LoadAsync(CancellationToken cancellationToken)
    {
        return await ExecuteWithRetryAsync(async () =>
        {
            using var client = CreateClient();

            try
            {
                using var response = await client.GetObjectAsync(new GetObjectRequest
                {
                    BucketName = _options.BucketName,
                    Key = RegistryObjectKey
                }, cancellationToken);

                var servers = await JsonSerializer.DeserializeAsync<List<ServerContainer>>(
                    response.ResponseStream,
                    SerializerOptions,
                    cancellationToken);

                return (IReadOnlyList<ServerContainer>)(servers ?? []);
            }
            catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                // Объекта ещё нет — например самый первый запуск. Это не ошибка, реестр просто пуст.
                return (IReadOnlyList<ServerContainer>)[];
            }
        }, cancellationToken);
    }

    public async Task SaveAsync(IReadOnlyList<ServerContainer> servers, CancellationToken cancellationToken)
    {
        await ExecuteWithRetryAsync(async () =>
        {
            using var client = CreateClient();
            var json = JsonSerializer.Serialize(servers, SerializerOptions);

            await client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _options.BucketName,
                Key = RegistryObjectKey,
                ContentBody = json,
                ContentType = "application/json"
            }, cancellationToken);

            _logger.LogDebug("Saved {Count} server registry records to S3 key {Key}.", servers.Count, RegistryObjectKey);
        }, cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await ExecuteWithRetryAsync(async () =>
        {
            using var client = CreateClient();

            try
            {
                await client.DeleteObjectAsync(_options.BucketName, RegistryObjectKey, cancellationToken);
            }
            catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                // Уже отсутствует — чистить нечего.
            }
        }, cancellationToken);
    }

    private AmazonS3Client CreateClient()
    {
        var config = new AmazonS3Config
        {
            ServiceURL = S3SaveStorage.NormalizeEndpointForAwsSdk(_options.EndPoint),
            ForcePathStyle = true
        };

        return new AmazonS3Client(_options.AccessKey, _options.SecretKey, config);
    }

    private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        const int attempts = 3;
        Exception? lastException = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (AmazonS3Exception exception) when (attempt < attempts)
            {
                lastException = exception;
                _logger.LogWarning(exception, "S3 registry operation failed on attempt {Attempt}.", attempt);
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
        }

        throw new InvalidOperationException("S3 registry operation failed after retries.", lastException);
    }

    private async Task ExecuteWithRetryAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        await ExecuteWithRetryAsync(async () =>
        {
            await operation();
            return true;
        }, cancellationToken);
    }
}