using Microsoft.Extensions.Options;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Metrics;

/// <summary>Startup-only telemetry configuration. Credentials are supplied by deployment secrets.</summary>
public sealed class MetricsOptions
{
    public const string SectionName = "Metrics";

    public bool Enabled { get; init; }
    public int ExportIntervalMilliseconds { get; init; } = 10_000;
    public string? OtlpMetricsEndpoint { get; init; }
    public string? OtlpAuthorization { get; init; }
    public string ServiceName { get; init; } = "transitjazz-transit-worker";
    public string Environment { get; init; } = "production";
    public bool LocalPrometheusEnabled { get; init; }

    public void Validate(int workerIntervalSeconds, bool isProduction)
    {
        var failures = new List<string>();
        if (ExportIntervalMilliseconds <= 0)
            failures.Add("ExportIntervalMilliseconds must be positive.");
        if (ExportIntervalMilliseconds > workerIntervalSeconds * 1_000)
            failures.Add("ExportIntervalMilliseconds must not exceed the worker cycle interval.");
        if (string.IsNullOrWhiteSpace(ServiceName))
            failures.Add("ServiceName is required.");
        if (string.IsNullOrWhiteSpace(Environment))
            failures.Add("Environment is required.");
        if (isProduction && LocalPrometheusEnabled)
            failures.Add("LocalPrometheusEnabled is not allowed in production.");

        if (Enabled && !LocalPrometheusEnabled)
        {
            if (!Uri.TryCreate(OtlpMetricsEndpoint, UriKind.Absolute, out var endpoint)
                || endpoint.Scheme != Uri.UriSchemeHttps
                || !endpoint.AbsolutePath.EndsWith("/v1/metrics", StringComparison.Ordinal))
                failures.Add("OtlpMetricsEndpoint must be an HTTPS endpoint ending in /v1/metrics.");
            if (string.IsNullOrWhiteSpace(OtlpAuthorization))
                failures.Add("OtlpAuthorization is required when Cloud metrics export is enabled.");
        }

        if (failures.Count > 0)
            throw new OptionsValidationException(SectionName, typeof(MetricsOptions), failures);
    }
}
