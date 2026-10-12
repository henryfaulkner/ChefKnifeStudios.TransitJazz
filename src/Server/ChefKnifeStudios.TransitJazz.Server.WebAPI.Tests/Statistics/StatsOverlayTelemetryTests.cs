using System.Diagnostics.Metrics;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class StatsOverlayTelemetryTests
{
    [Fact]
    public void MeasurementsUseFiniteTagsAndNeverExposeCityOrRouteIdentity()
    {
        var measurements = new List<(string Name, object? Value, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == StatsOverlayTelemetry.MeterName)
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, tags.ToArray())));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, tags.ToArray())));
        listener.Start();

        StatsOverlayTelemetry.RecordEndpointRequest("city:private-city", "429", "secret-route-join-key",
            TimeSpan.FromMilliseconds(12), 2048);
        StatsOverlayTelemetry.RecordCacheEvent("city:private-city", "hit");
        StatsOverlayTelemetry.RecordReadDuration("all", TimeSpan.FromMilliseconds(25));
        StatsOverlayTelemetry.RecordResponseBytes("all", 4096);

        Assert.Contains(measurements, measurement => measurement.Name == "transitjazz.stats.requests");
        Assert.Contains(measurements, measurement => measurement.Name == "transitjazz.stats.rejections");
        Assert.Contains(measurements, measurement => measurement.Name == "transitjazz.stats.cache.events");
        Assert.Contains(measurements, measurement => measurement.Name == "transitjazz.stats.read.duration");
        Assert.Contains(measurements, measurement => measurement.Name == "transitjazz.stats.response.bytes");
        Assert.All(measurements, measurement =>
        {
            var renderedTags = string.Join(";", measurement.Tags.Select(tag => $"{tag.Key}={tag.Value}"));
            Assert.DoesNotContain("private-city", renderedTags, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-route-join-key", renderedTags, StringComparison.Ordinal);
        });
        Assert.Contains(measurements.SelectMany(measurement => measurement.Tags), tag => tag.Key == "scope" && Equals(tag.Value, "unknown"));
        Assert.Contains(measurements.SelectMany(measurement => measurement.Tags), tag => tag.Key == "code" && Equals(tag.Value, "other"));
    }
}
