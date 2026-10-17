using System.Threading.Channels;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using ChefKnifeStudios.TransitJazz.Server.Data.Statistics;
using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;

/// <summary>Bounded nonblocking persistence queue. Storage failure never blocks polling.</summary>
public sealed class ReportedVehicleStatisticsWriter : BackgroundService, IReportedVehicleStatisticsSink
{
    readonly CityReportedVehicleHoursStore _store;
    readonly ReportedVehicleHistoryOptions _options;
    readonly ILogger<ReportedVehicleStatisticsWriter> _logger;
    readonly Channel<QueuedEnvelope> _queue;
    readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ReportedVehicleStatisticsWriter(CityReportedVehicleHoursStore store,
        ReportedVehicleHistoryOptions options, ILogger<ReportedVehicleStatisticsWriter> logger)
    {
        _store = store;
        _options = options;
        _logger = logger;
        _queue = Channel.CreateBounded<QueuedEnvelope>(new BoundedChannelOptions(options.MaxQueuedBatches)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    }

    public bool TryEnqueue(FinalizedReportedVehicleHourBatch batch, IDisposable reservation) =>
        _queue.Writer.TryWrite(new QueuedEnvelope(batch, reservation));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                using (item.Reservation)
                    await WriteWithRetryAsync(item.Batch, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            while (_queue.Reader.TryRead(out var item)) item.Reservation.Dispose();
            _drained.TrySetResult();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _queue.Writer.TryComplete();
        try
        {
            await _drained.Task.WaitAsync(TimeSpan.FromSeconds(_options.ShutdownDrainSeconds), cancellationToken);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Reported-vehicle writer drain exceeded {Seconds} seconds; queued envelopes will be released.",
                _options.ShutdownDrainSeconds);
        }
        await base.StopAsync(cancellationToken);
    }

    async Task WriteWithRetryAsync(FinalizedReportedVehicleHourBatch batch, CancellationToken stoppingToken)
    {
        for (var attempt = 1; attempt <= _options.MaxWriteAttempts; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.WriteAttemptTimeoutSeconds));
            try
            {
                var result = await _store.WriteAsync(ToHeader(batch), ToMemberships(batch), timeout.Token);
                if (result == ReportedVehicleWriteResult.Quarantined)
                    _logger.LogWarning("Reported-vehicle cohort quarantined for {City} hour={HourStartUtc}; run={CaptureRunId}.",
                        batch.Header.CitySlug, batch.Header.HourStartUtc, batch.Header.CaptureRunId);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning("Reported-vehicle write attempt {Attempt} failed for {City} hour={HourStartUtc}; exception type {ExceptionType}.",
                    attempt, batch.Header.CitySlug, batch.Header.HourStartUtc, exception.GetType().Name);
                if (attempt == _options.MaxWriteAttempts) return;
                try { await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
            }
        }
    }

    static CityReportedVehicleHour ToHeader(FinalizedReportedVehicleHourBatch batch)
    {
        var header = batch.Header;
        return new CityReportedVehicleHour
        {
            CitySlug = header.CitySlug,
            HourStartUtc = header.HourStartUtc,
            DefinitionVersion = header.DefinitionVersion,
            CaptureRunId = header.CaptureRunId,
            SourceSetFingerprint = header.SourceSetFingerprint,
            CadencePolicyFingerprint = header.CadencePolicyFingerprint,
            CollectionStatus = Enum.Parse<ChefKnifeStudios.TransitJazz.Server.Data.Models.ReportedVehicleCollectionStatus>(header.CollectionStatus.ToString()),
            IdentityIntegrity = header.IdentityIntegrity,
            HasConflict = header.HasConflict,
            MembershipCount = header.MembershipCount,
            SuccessfulPollCount = header.SuccessfulPollCount,
            FailedPollCount = header.FailedPollCount,
            UnidentifiableRecordCount = header.UnidentifiableRecordCount,
            FallbackRecordCount = header.FallbackRecordCount,
            ExpectedSourceCount = header.ExpectedSourceCount,
            SourceDiagnosticsJson = JsonSerializer.Serialize(header.SourceDiagnostics),
            IncompleteReasons = header.IncompleteReasons.ToArray(),
            IdentityLimitations = header.IdentityLimitations.ToArray(),
            ObservedStartUtc = header.ObservedStartUtc,
            ObservedEndUtc = header.ObservedEndUtc,
            PersistedAtUtc = header.PersistedAtUtc,
        };
    }

    static CityReportedVehicleHourMembership[] ToMemberships(FinalizedReportedVehicleHourBatch batch) =>
        batch.Memberships.Select(identity => new CityReportedVehicleHourMembership
        {
            CitySlug = batch.Header.CitySlug,
            HourStartUtc = batch.Header.HourStartUtc,
            IdentityNamespace = identity.Namespace,
            IdentityKind = identity.Kind,
            IdentityValue = identity.Value,
        }).ToArray();

    sealed record QueuedEnvelope(FinalizedReportedVehicleHourBatch Batch, IDisposable Reservation);
}
