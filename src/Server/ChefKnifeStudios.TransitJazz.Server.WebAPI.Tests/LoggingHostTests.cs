using System.Text.Json;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests;

public sealed class LoggingHostTests
{
    [Fact]
    public void ProductionConfiguration_SuppressesRoutineNoiseButKeepsStructuredEvents()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(AppSettingsPath()));
        var levels = document.RootElement.GetProperty("Logging").GetProperty("LogLevel");

        Assert.Equal("Warning", levels.GetProperty("Default").GetString());
        Assert.Equal("Information", levels.GetProperty("ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging.StructuredEventEmitter").GetString());
    }

    [Fact]
    public void StructuredKillSwitchAndLegacyMetricsSettingsRemainPresent()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(AppSettingsPath()));
        var logging = document.RootElement.GetProperty("Logging");
        Assert.True(logging.GetProperty("Structured").GetProperty("Enabled").GetBoolean());
        Assert.True(logging.GetProperty("Telemetry").GetProperty("Enabled").GetBoolean());
        Assert.True(document.RootElement.GetProperty("Metrics").GetProperty("Enabled").ValueKind is JsonValueKind.True or JsonValueKind.False);
    }

    [Fact]
    public void HostsRegisterUtcJsonConsoleAndStructuredEmitter()
    {
        var workerProgram = File.ReadAllText(WorkerProgramPath());
        var webApiProgram = File.ReadAllText(WebApiProgramPath());

        Assert.Contains("AddJsonConsole", workerProgram, StringComparison.Ordinal);
        Assert.Contains("UseUtcTimestamp = true", workerProgram, StringComparison.Ordinal);
        Assert.Contains("AddJsonConsole", webApiProgram, StringComparison.Ordinal);
        Assert.Contains("UseUtcTimestamp = true", webApiProgram, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<IWorkerStructuredEventLogger, StructuredEventEmitter>", webApiProgram, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonConsoleFormatter_WritesOneLineUtcTimestampAndMessage()
    {
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            using var loggerFactory = LoggerFactory.Create(builder => builder.AddJsonConsole(options =>
            {
                options.IncludeScopes = false;
                options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
                options.UseUtcTimestamp = true;
            }));
            loggerFactory.CreateLogger("formatter-test").LogInformation("formatter marker");
        }
        finally
        {
            Console.SetOut(original);
        }

        var line = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Single();
        using var document = JsonDocument.Parse(line);
        Assert.Equal("formatter marker", document.RootElement.GetProperty("Message").GetString());
        Assert.EndsWith("Z", document.RootElement.GetProperty("Timestamp").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DisabledStructuredEvents_DoNotAddConsoleNoise()
    {
        using var provider = new CountingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var emitter = new StructuredEventEmitter(
            loggerFactory.CreateLogger<StructuredEventEmitter>(),
            Options.Create(new StructuredLoggingOptions { Enabled = false }),
            new StructuredEventPolicy());

        emitter.Emit(StructuredLogEvent.Create(StructuredLogEventName.CityCycleAnomaly, StructuredLogOutcome.Failed,
            StructuredLogEvent.NewEventId(), "disabled-cycle", "atlanta", StructuredLogReasonCode.NO_VEHICLES));

        Assert.Equal(0, provider.Count);
    }

    static string AppSettingsPath() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "ChefKnifeStudios.TransitJazz.Server.WebAPI", "appsettings.json"));

    static string WorkerProgramPath() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "ChefKnifeStudios.TransitJazz.Server.TransitDataWorker", "Program.cs"));

    static string WebApiProgramPath() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "ChefKnifeStudios.TransitJazz.Server.WebAPI", "Program.cs"));

    sealed class CountingLoggerProvider : ILoggerProvider
    {
        public int Count;
        public ILogger CreateLogger(string categoryName) => new CountingLogger(this);
        public void Dispose() { }

        sealed class CountingLogger(CountingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => Interlocked.Increment(ref owner.Count);
        }

        sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
