using System.Text;
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

    public async Task<IReadOnlyList<ServerContainer>> LoadAsync(CancellationToken cancellationToken)
    {
        return await ExecuteWithRetryAsync(async () =>
        {
            using var client = CreateClient();
            var servers = new List<ServerContainer>();
            var listRequest = new ListObjectsV2Request
            {
                BucketName = _options.BucketName,
                Prefix = _options.ServersListSavesPath
            };

            ListObjectsV2Response listResponse;
            do
            {
                listResponse = await client.ListObjectsV2Async(listRequest, cancellationToken);

                foreach (var s3Object in listResponse.S3Objects.Where(item => !item.Key.EndsWith('/')))
                {
                    using var response = await client.GetObjectAsync(new GetObjectRequest
                    {
                        BucketName = _options.BucketName,
                        Key = s3Object.Key
                    }, cancellationToken);

                    var server = await JsonSerializer.DeserializeAsync<ServerContainer>(
                        response.ResponseStream,
                        SerializerOptions,
                        cancellationToken);

                    if (server is not null)
                    {
                        servers.Add(server);
                    }
                }

                listRequest.ContinuationToken = listResponse.NextContinuationToken;
            }
            while (listResponse.IsTruncated);

            return (IReadOnlyList<ServerContainer>)servers;
        }, cancellationToken);
    }

    public async Task SaveAsync(IReadOnlyList<ServerContainer> servers, CancellationToken cancellationToken)
    {
        await ExecuteWithRetryAsync(async () =>
        {
            using var client = CreateClient();
            await ClearPrefixAsync(client, cancellationToken);

            for (var index = 0; index < servers.Count; index++)
            {
                var key = S3SaveStorage.CombineS3Key(_options.ServersListSavesPath, $"{index + 1}server.json");
                var json = JsonSerializer.Serialize(servers[index], SerializerOptions);

                await client.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = _options.BucketName,
                    Key = key,
                    ContentBody = json,
                    ContentType = "application/json"
                }, cancellationToken);
            }

            _logger.LogDebug("Saved {Count} server registry records to S3 prefix {Prefix}.", servers.Count, _options.ServersListSavesPath);
        }, cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await ExecuteWithRetryAsync(async () =>
        {
            using var client = CreateClient();
            await ClearPrefixAsync(client, cancellationToken);
        }, cancellationToken);
    }

    private async Task ClearPrefixAsync(AmazonS3Client client, CancellationToken cancellationToken)
    {
        var listRequest = new ListObjectsV2Request
        {
            BucketName = _options.BucketName,
            Prefix = _options.ServersListSavesPath
        };

        ListObjectsV2Response listResponse;
        do
        {
            listResponse = await client.ListObjectsV2Async(listRequest, cancellationToken);
            var objectsToDelete = listResponse.S3Objects
                .Where(item => !item.Key.EndsWith('/'))
                .Select(item => new KeyVersion { Key = item.Key })
                .ToList();

            if (objectsToDelete.Count > 0)
            {
                await client.DeleteObjectsAsync(new DeleteObjectsRequest
                {
                    BucketName = _options.BucketName,
                    Objects = objectsToDelete
                }, cancellationToken);
            }

            listRequest.ContinuationToken = listResponse.NextContinuationToken;
        }
        while (listResponse.IsTruncated);
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
