using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Metrics;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;

/// <summary>
/// Bounded facts from one city tick. This is an in-process hand-off to event classification, not
/// a replacement for the existing metric or Parquet records.
/// </summary>
public sealed record CityCycleOutcome
{
    public required string City { get; init; }
    public required CityFetchResult Fetch { get; init; }
    public required Worker.CityTickResult Tick { get; init; }
    public required bool RouteIndexAvailable { get; init; }
    public required bool DuplicateFeed { get; init; }
    public bool PublishAttempted => Tick.PublishAttempted;
    public bool? PublishSucceeded => Tick.PublishSucceeded;
    public string? ExceptionType { get; init; }
}

