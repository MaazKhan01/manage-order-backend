namespace DmOrder.Api.Configuration;

/// <summary>
/// Maps the flat environment-variable names used in <c>.env.example</c> and on hosting platforms onto
/// the nested configuration keys the application binds to.
///
/// Without this you would have to write <c>ConnectionStrings__Default</c> in every environment, which
/// is easy to get wrong and unfamiliar to anyone who is not a .NET developer. The double-underscore
/// form still works and takes precedence, because it is set later by AddEnvironmentVariables.
/// </summary>
public static class EnvironmentVariableMappings
{
    private static readonly (string EnvVar, string ConfigKey)[] Mappings =
    [
        ("DATABASE_CONNECTION_STRING", "ConnectionStrings:Default"),
        ("JWT_SECRET", "Jwt:Secret"),
        ("JWT_ISSUER", "Jwt:Issuer"),
        ("JWT_AUDIENCE", "Jwt:Audience"),
        ("AI_API_KEY", "Ai:ApiKey"),
        ("AI_MODEL", "Ai:Model"),
        ("OBJECT_STORAGE_ENDPOINT", "FileStorage:S3:Endpoint"),
        ("OBJECT_STORAGE_ACCESS_KEY", "FileStorage:S3:AccessKey"),
        ("OBJECT_STORAGE_SECRET_KEY", "FileStorage:S3:SecretKey"),
        ("OBJECT_STORAGE_BUCKET", "FileStorage:S3:Bucket"),
        ("FILE_STORAGE_PROVIDER", "FileStorage:Provider"),
        ("FILE_STORAGE_PUBLIC_BASE_URL", "FileStorage:PublicBaseUrl"),
        ("PLATFORM_URL", "PlatformBranding:PlatformUrl"),
        ("PROJECT_NAME", "PlatformBranding:ProjectName"),
        ("PROJECT_SHORT_NAME", "PlatformBranding:ProjectShortName"),
        ("PROJECT_DESCRIPTION", "PlatformBranding:ProjectDescription"),
        ("SUPPORT_EMAIL", "PlatformBranding:SupportEmail"),
        ("SEED_ADMIN_EMAIL", "Seed:AdminEmail"),
        ("SEED_ADMIN_PASSWORD", "Seed:AdminPassword"),
    ];

    public static IEnumerable<KeyValuePair<string, string?>> Collect()
    {
        foreach (var (envVar, configKey) in Mappings)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return new KeyValuePair<string, string?>(configKey, value);
            }
        }

        // A list needs indexed keys, so a comma-separated variable is expanded here.
        var origins = Environment.GetEnvironmentVariable("FRONTEND_ORIGINS");
        if (!string.IsNullOrWhiteSpace(origins))
        {
            var index = 0;
            foreach (var origin in origins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                yield return new KeyValuePair<string, string?>($"Frontend:AllowedOrigins:{index++}", origin);
            }
        }
    }
}
