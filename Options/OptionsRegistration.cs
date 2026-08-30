using NodaTime;

namespace SurvivalBackend.Options;

public static class OptionsRegistration
{
    public static IServiceCollection AddSurvivalOptions(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<EdgegapOptions>()
            .Bind(configuration.GetSection(EdgegapOptions.SectionName))
            .PostConfigure(options =>
            {
                options.Token = FirstNotEmpty(options.Token, configuration["EdgegapToken"]);
            })
            .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _), "Edgegap:BaseUrl must be an absolute URL.")
            .Validate(options => options.TimeoutSeconds is >= 1 and <= 120, "Edgegap:TimeoutSeconds must be between 1 and 120.")
            .Validate(options => options.RetryAttempts is >= 1 and <= 10, "Edgegap:RetryAttempts must be between 1 and 10.")
            .Validate(options => options.RetryBaseDelayMs is >= 100 and <= 10000, "Edgegap:RetryBaseDelayMs must be between 100 and 10000.")
            .Validate(options => environment.IsDevelopment() || !string.IsNullOrWhiteSpace(options.Token), "Edgegap:Token is required outside Development.")
            .ValidateOnStart();

        services.AddOptions<S3Options>()
            .Bind(configuration.GetSection(S3Options.SectionName))
            .PostConfigure(options =>
            {
                options.EndPoint = FirstNotEmpty(options.EndPoint, configuration["S3EndPoint"]);
                options.BucketName = FirstNotEmpty(options.BucketName, configuration["S3BucketName"]);
                options.AccessKey = FirstNotEmpty(options.AccessKey, configuration["S3AccessKey"]);
                options.SecretKey = FirstNotEmpty(options.SecretKey, configuration["S3SecretKey"]);
                options.CurrentWipeSavesPath = EnsureTrailingSlash(FirstNotEmpty(options.CurrentWipeSavesPath, configuration["S3CurrentWipeSavesPath"]));
                options.ServersListSavesPath = EnsureTrailingSlash(FirstNotEmpty(options.ServersListSavesPath, configuration["S3ServersListSavesPath"]));
            })
            .Validate(options => IsCredentialDeliveryModeValid(options.CredentialDeliveryMode), "S3:CredentialDeliveryMode must be PresignedUrls or RawCredentials.")
            .Validate(options => options.PresignedUrlExpirationMinutes is >= 1 and <= 1440, "S3:PresignedUrlExpirationMinutes must be between 1 and 1440.")
            .Validate(options => environment.IsDevelopment() || !string.IsNullOrWhiteSpace(options.EndPoint), "S3:EndPoint is required outside Development.")
            .Validate(options => environment.IsDevelopment() || !string.IsNullOrWhiteSpace(options.BucketName), "S3:BucketName is required outside Development.")
            .Validate(options => environment.IsDevelopment() || !string.IsNullOrWhiteSpace(options.AccessKey), "S3:AccessKey is required outside Development.")
            .Validate(options => environment.IsDevelopment() || !string.IsNullOrWhiteSpace(options.SecretKey), "S3:SecretKey is required outside Development.")
            .ValidateOnStart();

        services.AddOptions<SecurityOptions>()
            .Bind(configuration.GetSection(SecurityOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ServerApiKeyHeaderName), "Security:ServerApiKeyHeaderName is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.AdminApiKeyHeaderName), "Security:AdminApiKeyHeaderName is required.")
            .Validate(options => !options.RequireApiKeys || !string.IsNullOrWhiteSpace(options.ServerApiKey), "Security:ServerApiKey is required when Security:RequireApiKeys is true.")
            .Validate(options => !options.RequireApiKeys || !string.IsNullOrWhiteSpace(options.AdminApiKey), "Security:AdminApiKey is required when Security:RequireApiKeys is true.")
            .ValidateOnStart();

        services.AddOptions<GameClientOptions>()
            .Bind(configuration.GetSection(GameClientOptions.SectionName))
            .PostConfigure(options =>
            {
                options.CurrentVersion = FirstNotEmpty(options.CurrentVersion, configuration["gameclientversion"], configuration["GameClientVersion"]);
            })
            .Validate(options => !string.IsNullOrWhiteSpace(options.CurrentVersion), "GameClient:CurrentVersion is required.")
            .Validate(options => options.CurrentVersion.Length <= 64, "GameClient:CurrentVersion must be 64 characters or fewer.")
            .ValidateOnStart();

        services.AddOptions<WipeOptions>()
            .Bind(configuration.GetSection(WipeOptions.SectionName))
            .PostConfigure(options =>
            {
                options.DayOfWeek = FirstNotEmpty(options.DayOfWeek, configuration["dayofweek"], configuration["DayOfWeek"]);
                options.Time = FirstNotEmpty(options.Time, configuration["time"], configuration["Time"]);
                options.TimeZone = FirstNotEmpty(options.TimeZone, configuration["timezone"], configuration["TimeZone"]);
            })
            .Validate(options => Enum.TryParse<IsoDayOfWeek>(options.DayOfWeek, ignoreCase: true, out _), "Wipe:DayOfWeek must be a valid ISO day of week.")
            .Validate(options => LocalTimePatternHelper.TryParse(options.Time, out _), "Wipe:Time must use HH:mm format.")
            .Validate(options => DateTimeZoneProviders.Tzdb.GetZoneOrNull(options.TimeZone) is not null, "Wipe:TimeZone must be a valid TZDB time zone.")
            .ValidateOnStart();

        services.AddOptions<ServerRegistryOptions>()
            .Bind(configuration.GetSection(ServerRegistryOptions.SectionName))
            .Validate(options => IsStorageModeValid(options.StorageMode), "ServerRegistry:StorageMode must be S3 or LocalFile.")
            .Validate(options => options.StaleServerStateSeconds is >= 10 and <= 3600, "ServerRegistry:StaleServerStateSeconds must be between 10 and 3600.")
            .ValidateOnStart();

        return services;
    }

    private static string FirstNotEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string EnsureTrailingSlash(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return value.EndsWith('/') ? value : value + "/";
    }

    private static bool IsCredentialDeliveryModeValid(string mode)
    {
        return string.Equals(mode, "PresignedUrls", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "RawCredentials", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStorageModeValid(string mode)
    {
        return string.Equals(mode, "S3", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "LocalFile", StringComparison.OrdinalIgnoreCase);
    }

    private static class LocalTimePatternHelper
    {
        public static bool TryParse(string value, out LocalTime localTime)
        {
            localTime = default;

            var parts = value.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                return false;
            }

            if (!int.TryParse(parts[0], out var hour) || !int.TryParse(parts[1], out var minute))
            {
                return false;
            }

            if (hour is < 0 or > 23 || minute is < 0 or > 59)
            {
                return false;
            }

            localTime = new LocalTime(hour, minute);
            return true;
        }
    }
}
