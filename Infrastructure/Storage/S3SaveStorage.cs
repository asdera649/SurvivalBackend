using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using SurvivalBackend.Contracts;
using SurvivalBackend.Options;

namespace SurvivalBackend.Infrastructure.Storage;

public sealed class S3SaveStorage(
    IOptions<S3Options> options,
    ILogger<S3SaveStorage> logger) : ISaveStorage
{
    private readonly S3Options _options = options.Value;
    private readonly ILogger<S3SaveStorage> _logger = logger;

    public ServerRegistrationData CreateServerSaveAccess(string serverName)
    {
        var objectKey = CombineS3Key(_options.CurrentWipeSavesPath, $"{serverName}.json");
        var mode = _options.CredentialDeliveryMode;

        if (string.Equals(mode, "RawCredentials", StringComparison.OrdinalIgnoreCase))
        {
            return new ServerRegistrationData
            {
                EndPoint = _options.EndPoint,
                BucketName = _options.BucketName,
                ObjectKey = objectKey,
                AccessKey = _options.AccessKey,
                SecretKey = _options.SecretKey
            };
        }

        using var client = CreateClient();
        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(_options.PresignedUrlExpirationMinutes);

        var downloadUrl = client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Expires = expiresAtUtc.UtcDateTime
        });

        var uploadUrl = client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            Expires = expiresAtUtc.UtcDateTime
        });

        return new ServerRegistrationData
        {
            EndPoint = _options.EndPoint,
            BucketName = _options.BucketName,
            ObjectKey = objectKey,
            DownloadUrl = downloadUrl,
            UploadUrl = uploadUrl,
            UrlExpiresAtUtc = expiresAtUtc
        };
    }

    public async Task ClearCurrentWipeSavesAsync(CancellationToken cancellationToken)
    {
        await ExecuteWithRetryAsync(async () =>
        {
            using var client = CreateClient();
            var listRequest = new ListObjectsV2Request
            {
                BucketName = _options.BucketName,
                Prefix = _options.CurrentWipeSavesPath
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
                    var deleteResponse = await client.DeleteObjectsAsync(new DeleteObjectsRequest
                    {
                        BucketName = _options.BucketName,
                        Objects = objectsToDelete
                    }, cancellationToken);

                    _logger.LogInformation(
                        "Removed {Count} current wipe save objects from S3 prefix {Prefix}.",
                        deleteResponse.DeletedObjects.Count,
                        _options.CurrentWipeSavesPath);
                }

                listRequest.ContinuationToken = listResponse.NextContinuationToken;
            }
            while (listResponse.IsTruncated);
        }, cancellationToken);
    }

    private AmazonS3Client CreateClient()
    {
        var config = new AmazonS3Config
        {
            ServiceURL = NormalizeEndpointForAwsSdk(_options.EndPoint),
            ForcePathStyle = true
        };

        return new AmazonS3Client(_options.AccessKey, _options.SecretKey, config);
    }

    private async Task ExecuteWithRetryAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        const int attempts = 3;
        Exception? lastException = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                await operation();
                return;
            }
            catch (AmazonS3Exception exception) when (attempt < attempts)
            {
                lastException = exception;
                _logger.LogWarning(exception, "S3 operation failed on attempt {Attempt}.", attempt);
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
        }

        throw new InvalidOperationException("S3 operation failed after retries.", lastException);
    }

    internal static string CombineS3Key(string prefix, string fileName)
    {
        return string.IsNullOrWhiteSpace(prefix)
            ? fileName
            : prefix.TrimEnd('/') + "/" + fileName;
    }

    internal static string NormalizeEndpointForAwsSdk(string endpoint)
    {
        if (endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return endpoint;
        }

        return $"https://{endpoint}";
    }
}
