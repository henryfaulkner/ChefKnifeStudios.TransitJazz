using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests;

public sealed record CapturedStructuredLog(
    LogLevel Level,
    EventId EventId,
    string Message,
    IReadOnlyDictionary<string, object?> Fields,
    string Json);

/// <summary>Small in-memory provider for asserting structured fields without a console or Azure.</summary>
public sealed class InMemoryJsonLoggerProvider : ILoggerProvider
{
    readonly ConcurrentQueue<CapturedStructuredLog> _entries = new();
    public IReadOnlyList<CapturedStructuredLog> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new Logger(_entries, categoryName);
    public void Dispose() { }

    sealed class Logger(ConcurrentQueue<CapturedStructuredLog> entries, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var fields = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.Where(p => p.Key != "{OriginalFormat}").ToDictionary(p => p.Key, p => p.Value)
                : new Dictionary<string, object?> { ["Message"] = formatter(state, exception) };
            fields["Category"] = categoryName;
            var message = fields.TryGetValue("EventName", out var eventName)
                ? eventName?.ToString() ?? "StructuredLogEvent"
                : formatter(state, exception);
            entries.Enqueue(new CapturedStructuredLog(logLevel, eventId, message, fields,
                JsonSerializer.Serialize(fields)));
        }
    }

    sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}

/// <summary>Deterministic UTC clock used by policy tests.</summary>
public sealed class FakeTimeProvider(DateTimeOffset initialUtc) : TimeProvider
{
    DateTimeOffset _utcNow = initialUtc.ToUniversalTime();
    public override DateTimeOffset GetUtcNow() => _utcNow;
    public void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
    public void SetUtcNow(DateTimeOffset value) => _utcNow = value.ToUniversalTime();
}

