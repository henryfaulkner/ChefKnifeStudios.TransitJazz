using Xunit;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests;

public class PartitionPathTests
{
    [Theory]
    [InlineData("snap")]
    [InlineData("lerp")]
    [InlineData("cycle")]
    public void BlobPath_HasCorrectStructure(string dataset)
    {
        var path = BuildBlobPath(dataset, DateTime.UtcNow);

        // Must start with dataset/
        Assert.StartsWith($"{dataset}/", path);

        // Must contain dt=YYYY-MM-DD partition
        Assert.Contains("/dt=", path);
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
        var path = BuildBlobPath("cycle", utcNow);

        Assert.Contains("dt=2026-06-04", path);
    }

    [Fact]
    public void TwoCallsInSameMillisecond_ProduceUniquePaths()
    {
        var now = DateTime.UtcNow;
        var path1 = BuildBlobPath("snap", now);
        var path2 = BuildBlobPath("snap", now);

        // Short guid suffix makes them unique even at identical timestamps
        Assert.NotEqual(path1, path2);
    }

    static string BuildBlobPath(string dataset, DateTime utcNow)
    {
        var shortGuid = Guid.NewGuid().ToString("N")[..8];
        return $"{dataset}/dt={utcNow:yyyy-MM-dd}/part-{utcNow:yyyyMMddTHHmmssfffZ}-{shortGuid}.parquet";
    }
}
