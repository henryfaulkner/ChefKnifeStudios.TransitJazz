namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;

/// <summary>Bound from <c>Logging:Telemetry:*</c> in configuration.</summary>
public sealed class LoggingOptions
{
    /// <summary>Azure Blob service endpoint, e.g. https://&lt;account&gt;.blob.core.windows.net</summary>
    public string BlobServiceUri { get; set; } = string.Empty;

    /// <summary>Container name. Defaults to <c>telemetry</c>.</summary>
    public string Container { get; set; } = "telemetry";

    /// <summary>How often (seconds) to flush accumulated rows to blob. Default 300 (5 min).</summary>
    public int FlushIntervalSeconds { get; set; } = 300;

    /// <summary>Bounded channel capacity. Overflow events are dropped (DropWrite). Default 10 000.</summary>
    public int ChannelCapacity { get; set; } = 10_000;

    /// <summary>Kill switch. When false the sidecar registers no-ops.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Optional dev-only connection string. When set, takes precedence over
    /// <see cref="BlobServiceUri"/> + <c>DefaultAzureCredential</c>.
    /// Never commit a real key — use user-secrets or environment variable
    /// <c>Logging__Telemetry__ConnectionString</c> locally.
    /// </summary>
    public string? ConnectionString { get; set; }
}
