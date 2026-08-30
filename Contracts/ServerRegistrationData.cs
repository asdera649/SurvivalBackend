namespace SurvivalBackend.Contracts;

public sealed class ServerRegistrationData
{
    public required string EndPoint { get; init; }
    public required string BucketName { get; init; }
    public required string ObjectKey { get; init; }
    public string? AccessKey { get; init; }
    public string? SecretKey { get; init; }
    public string? DownloadUrl { get; init; }
    public string? UploadUrl { get; init; }
    public DateTimeOffset? UrlExpiresAtUtc { get; init; }
}
