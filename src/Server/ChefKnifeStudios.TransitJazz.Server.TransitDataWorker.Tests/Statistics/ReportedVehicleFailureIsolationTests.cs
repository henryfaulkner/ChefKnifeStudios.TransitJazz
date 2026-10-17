using System.Collections.Immutable;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.Statistics;

public sealed class ReportedVehicleFailureIsolationTests
{
    static readonly DateTime Hour = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueueAdmissionOrSinkFailureDoesNotEscapePollingOrLeakReservations(bool saturateQueue)
    {
        var limits = new ReportedVehicleHistoryOptions
        {
            MaxQueuedEnvelopeBytes = saturateQueue ? 1 : 1024 * 1024,
        };
        var source = new ReportedVehicleSourceDefinition
        {
            SourceKey = "feed-a", IdentityNamespace = "agency-a", IdentityKind = "descriptor",
            ExpectedCadenceSeconds = 30, GapLimitSeconds = 60,
        };
        var city = new ReportedVehicleSourceCatalogEntry("fixture-city", [source]);
        var catalog = new ReportedVehicleSourceCatalog([city]);
        var runtime = new ReportedVehicleHistoryRuntimeOptions(ReportedVehicleCaptureMode.DryRun,
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "fixture-city"), limits);
        var budget = new ReportedVehicleBufferBudget(limits);
        var sink = new FailingSink();
        var clock = new FakeTimeProvider(new DateTimeOffset(Hour.AddHours(3)));
        var capture = new ReportedVehicleCapture(runtime, catalog, budget, sink, clock,
            NullLogger<ReportedVehicleCapture>.Instance);

        capture.Observe("fixture-city", [Observation(source, Hour.AddSeconds(-30))]);
        for (var at = Hour; at <= Hour.AddHours(2); at = at.AddSeconds(30))
            capture.Observe("fixture-city", [Observation(source, at)]);

        Assert.True(capture.IsEnabledFor("fixture-city"));
        Assert.Equal(saturateQueue ? 0 : 3, sink.Attempts);
        Assert.Equal(0, budget.ReservedBytes(ReportedVehicleBufferKind.Extracted));
        Assert.Equal(0, budget.ReservedBytes(ReportedVehicleBufferKind.Queued));
    }

    static ReportedVehicleSourceObservation Observation(ReportedVehicleSourceDefinition source, DateTime at) =>
        ReportedVehicleSourceObservation.Successful(source, new DateTimeOffset(at), ["vehicle-a"]);

    sealed class FailingSink : IReportedVehicleStatisticsSink
    {
        public int Attempts { get; private set; }

        public bool TryEnqueue(FinalizedReportedVehicleHourBatch batch, IDisposable reservation)
        {
            Attempts++;
            throw new IOException("simulated storage sink failure");
        }
    }
}
