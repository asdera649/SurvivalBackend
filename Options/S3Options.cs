namespace SurvivalBackend.Options;

public sealed class S3Options
{
    public const string SectionName = "S3";

    public string EndPoint { get; set; } = string.Empty;
    public string BucketName { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string CurrentWipeSavesPath { get; set; } = string.Empty;
    public string ServersListSavesPath { get; set; } = string.Empty;
    public string CredentialDeliveryMode { get; set; } = "PresignedUrls";
    public int PresignedUrlExpirationMinutes { get; set; } = 15;
}
