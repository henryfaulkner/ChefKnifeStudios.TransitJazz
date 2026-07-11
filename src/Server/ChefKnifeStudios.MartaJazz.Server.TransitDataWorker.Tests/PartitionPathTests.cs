using Xunit;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests;

public class PartitionPathTests
{
    [Fact]
    public void BlobPath_HasCorrectStructure()
    {
        var path = BuildBlobPath(DateTime.UtcNow);

        // Must start with the "telemetry/" virtual directory (the dataset's fixed prefix
        // inside the configured container, which is not necessarily itself named "telemetry")
        Assert.StartsWith("telemetry/dt=", path);
        var dtSegment = path.Split('/').First(s => s.StartsWith("dt="));
        Assert.Matches(@"^dt=\d{4}-\d{2}-\d{2}$", dtSegment);

        // Must end with .parquet
        Assert.EndsWith(".parquet", path);

        // Part file must start with part-
        var fileName = Path.GetFileName(path);
        Assert.StartsWith("part-", fileName);
    }

    [Fact]
    public void BlobPath_UsesUtcDate()
    {
        var utcNow = new DateTime(2026, 6, 4, 23, 58, 0, DateTimeKind.Utc);
        var path = BuildBlobPath(utcNow);

        Assert.Contains("dt=2026-06-04", path);
    }

    [Fact]
    public void TwoCallsInSameMillisecond_ProduceUniquePaths()
    {
        var now = DateTime.UtcNow;
        var path1 = BuildBlobPath(now);
        var path2 = BuildBlobPath(now);

        // Short guid suffix makes them unique even at identical timestamps
        Assert.NotEqual(path1, path2);
    }

    static string BuildBlobPath(DateTime utcNow)
    {
        var shortGuid = Guid.NewGuid().ToString("N")[..8];
        return $"telemetry/dt={utcNow:yyyy-MM-dd}/part-{utcNow:yyyyMMddTHHmmssfffZ}-{shortGuid}.parquet";
    }
}
