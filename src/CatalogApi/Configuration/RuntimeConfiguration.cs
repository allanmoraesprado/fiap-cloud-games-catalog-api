namespace CatalogApi.Configuration;

// Runtime wiring that differs per environment:
// - Secrets mounted as files (Kubernetes; on AKS the Azure Key Vault CSI driver): one file per
//   key, "__" separating sections (e.g. Jwt__SecretKey -> Jwt:SecretKey). Enabled by setting
//   SecretsPath; loaded last, so mounted secrets override appsettings and environment variables.
//   Without SecretsPath (local runs) nothing changes.
// - The deployed version (commit SHA baked into the image as APP_VERSION), echoed in the
//   X-App-Version response header to trace commit -> image -> running pod.
public static class RuntimeConfiguration
{
    public const string SecretsPathKey = "SecretsPath";
    public const string VersionHeader = "X-App-Version";

    public static string AppVersion =>
        Environment.GetEnvironmentVariable("APP_VERSION") is { Length: > 0 } version ? version : "dev";

    public static IConfigurationBuilder AddMountedSecrets(this IConfigurationBuilder configuration, string? secretsPath)
    {
        if (string.IsNullOrWhiteSpace(secretsPath)) return configuration;

        // Fail fast: a configured but missing mount means the pod cannot get its credentials.
        if (!Directory.Exists(secretsPath))
            throw new InvalidOperationException($"Secrets directory '{secretsPath}' does not exist.");

        return configuration.AddKeyPerFile(secretsPath, optional: false, reloadOnChange: false);
    }

    public static IApplicationBuilder UseAppVersionHeader(this IApplicationBuilder app)
    {
        var version = AppVersion;
        return app.Use((context, next) =>
        {
            context.Response.Headers[VersionHeader] = version;
            return next(context);
        });
    }
}
