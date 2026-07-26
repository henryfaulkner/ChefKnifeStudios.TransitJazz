using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests;

/// <summary>
/// A minimal spy <see cref="ILogger"/> that counts log calls per level, so a test
/// can assert a warning path was taken (e.g. the D5b unmapped-route_type case)
/// without depending on the message text. Test-double = "spy" per
/// util-testing/test-doubles.md: a stub that also records calls.
/// </summary>
public sealed class WarningSpyLogger : ILogger
{
    public int WarningCount { get; private set; }
    public int ErrorCount { get; private set; }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Warning) WarningCount++;
        else if (logLevel == LogLevel.Error) ErrorCount++;
    }
}
