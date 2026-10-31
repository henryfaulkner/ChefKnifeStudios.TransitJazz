using ChefKnifeStudios.TransitJazz.Client.Core.Services;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class LiveDeliveryReadinessTests
{
    [Theory]
    [InlineData(LiveFailureStage.Start, LiveDeliveryFailureReason.StartFailed)]
    [InlineData(LiveFailureStage.Join, LiveDeliveryFailureReason.CityJoinFailed)]
    [InlineData(LiveFailureStage.Protocol, LiveDeliveryFailureReason.ProtocolRejected)]
    public async Task FailedInitialTransportOrCityJoinNeverReportsReady(
        LiveFailureStage failureStage,
        LiveDeliveryFailureReason expectedReason)
    {
        var connection = new FakeLiveDeliveryConnection(failureStage);
        var service = CreateService(connection);

        await service.InitAsync();

        Assert.False(service.DeliveryState.IsReady);
        Assert.Equal(expectedReason, service.DeliveryState.FailureReason);
        Assert.False(service.DeliveryState.CityJoined);
    }

    [Fact]
    public async Task MissingHubConfigurationNeverReportsReady()
    {
        var service = CreateService(new FakeLiveDeliveryConnection(LiveFailureStage.None), baseUri: null);

        await service.InitAsync();

        Assert.False(service.DeliveryState.IsReady);
        Assert.Equal(LiveDeliveryFailureReason.MissingConfiguration, service.DeliveryState.FailureReason);
    }

    [Fact]
    public async Task PendingCityJoinRemainsUnreadyUntilAcknowledged()
    {
        var connection = new FakeLiveDeliveryConnection(LiveFailureStage.None) { HoldJoin = true };
        var service = CreateService(connection);
        var initialization = service.InitAsync();

        await connection.JoinEntered.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(service.DeliveryState.TransportConnected);
        Assert.False(service.DeliveryState.CityJoined);
        Assert.False(service.DeliveryState.IsReady);

        connection.ReleaseJoin();
        await initialization.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(service.DeliveryState.IsReady);
        Assert.Null(service.DeliveryState.FailureReason);
    }

    [Fact]
    public async Task IndependentJoinDeadlineSettlesFailureWhileCallerCancellationStaysSilent()
    {
        var time = new NotificationTestTimeProvider();
        var connection = new FakeLiveDeliveryConnection(LiveFailureStage.None) { HoldJoin = true };
        var service = CreateService(connection, time);
        var initialization = service.InitAsync();
        try
        {
            await connection.JoinEntered.WaitAsync(TimeSpan.FromSeconds(1));
            time.Advance(TimeSpan.FromSeconds(15));
            time.DrainCallbacks();
            await initialization.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.False(service.DeliveryState.IsReady);
            Assert.Equal(LiveDeliveryFailureReason.OperationTimedOut, service.DeliveryState.FailureReason);
        }
        finally
        {
            connection.ReleaseJoin();
            await initialization;
        }
    }

    [Fact]
    public async Task CallerCancellationDoesNotBecomeAnEligibleTransportFailure()
    {
        var connection = new FakeLiveDeliveryConnection(LiveFailureStage.None) { HoldJoin = true };
        var service = CreateService(connection);
        using var cancellation = new CancellationTokenSource();
        var initialization = service.InitAsync(cancellation.Token);
        try
        {
            await connection.JoinEntered.WaitAsync(TimeSpan.FromSeconds(1));
            cancellation.Cancel();
            await initialization.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.False(service.DeliveryState.IsReady);
            Assert.Equal(LiveDeliveryFailureReason.Cancelled, service.DeliveryState.FailureReason);
        }
        finally
        {
            connection.ReleaseJoin();
            await initialization;
        }
    }

    [Fact]
    public async Task ReconnectingAndClosedInvalidateAcknowledgedMembership()
    {
        var connection = new FakeLiveDeliveryConnection(LiveFailureStage.None);
        var service = CreateService(connection);
        await service.InitAsync();
        Assert.True(service.DeliveryState.IsReady);

        await connection.RaiseReconnectingAsync();
        Assert.False(service.DeliveryState.TransportConnected);
        Assert.False(service.DeliveryState.CityJoined);
        Assert.False(service.DeliveryState.IsReady);

        await connection.RaiseReconnectedAsync();
        Assert.True(service.DeliveryState.IsReady);

        await connection.RaiseClosedAsync();
        Assert.False(service.DeliveryState.IsReady);
        Assert.Equal(LiveDeliveryFailureReason.ConnectionClosed, service.DeliveryState.FailureReason);
    }

    [Fact]
    public async Task FailedCurrentRejoinLeavesDeliveryUnavailable()
    {
        var connection = new FakeLiveDeliveryConnection(LiveFailureStage.None);
        var service = CreateService(connection);
        await service.InitAsync();
        connection.FailNextJoin = true;

        await connection.RaiseReconnectingAsync();
        await connection.RaiseReconnectedAsync();

        Assert.False(service.DeliveryState.IsReady);
        Assert.False(service.DeliveryState.CityJoined);
        Assert.Equal(LiveDeliveryFailureReason.ResumeFailed, service.DeliveryState.FailureReason);
        Assert.Equal(2, connection.JoinCallCount);
    }

    [Fact]
    public async Task PauseDuringHeldJoinLeavesAfterLateJoinAcknowledgement()
    {
        var connection = new FakeLiveDeliveryConnection(LiveFailureStage.None) { HoldJoin = true };
        var service = CreateService(connection);
        var initialization = service.InitAsync();
        await connection.JoinEntered.WaitAsync(TimeSpan.FromSeconds(1));

        service.DesiredDelivery = false;
        await service.PauseAsync();
        connection.ReleaseJoin();
        await initialization.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.False(service.DeliveryState.DeliveryDesired);
        Assert.False(service.DeliveryState.CityJoined);
        Assert.False(service.DeliveryState.IsReady);
        Assert.Equal(1, connection.LeaveCallCount);
    }

    [Fact]
    public async Task ReconnectWhilePausedDoesNotRejoin()
    {
        var connection = new FakeLiveDeliveryConnection(LiveFailureStage.None);
        var service = CreateService(connection);
        await service.InitAsync();
        service.DesiredDelivery = false;
        await service.PauseAsync();

        await connection.RaiseReconnectingAsync();
        await connection.RaiseReconnectedAsync();

        Assert.Equal(1, connection.JoinCallCount);
        Assert.False(service.DeliveryState.DeliveryDesired);
        Assert.False(service.DeliveryState.CityJoined);
        Assert.False(service.DeliveryState.IsReady);
    }

    [Fact]
    public async Task ReplacedConnectionIsStoppedDisposedAndCannotRunCurrentHandlers()
    {
        var oldConnection = new FakeLiveDeliveryConnection(LiveFailureStage.None);
        var newConnection = new FakeLiveDeliveryConnection(LiveFailureStage.None);
        var service = CreateService([oldConnection, newConnection]);
        await service.InitAsync();
        var oldGeneration = service.DeliveryState.ConnectionGeneration;

        await service.InitAsync();
        var current = service.DeliveryState;
        await oldConnection.RaiseReconnectingAsync();
        await oldConnection.RaiseReconnectedAsync();

        Assert.True(oldConnection.StopCallCount > 0);
        Assert.Equal(1, oldConnection.DisposeCallCount);
        Assert.True(current.ConnectionGeneration > oldGeneration);
        Assert.Equal(current, service.DeliveryState);
        Assert.Equal(1, oldConnection.JoinCallCount);
        Assert.Equal(1, newConnection.JoinCallCount);
    }

    static SignalRNotificationService CreateService(
        FakeLiveDeliveryConnection connection,
        TimeProvider? timeProvider = null,
        string? baseUri = "https://transit.example.test")
        => CreateService([connection], timeProvider, baseUri);

    static SignalRNotificationService CreateService(
        FakeLiveDeliveryConnection[] connections,
        TimeProvider? timeProvider = null,
        string? baseUri = "https://transit.example.test")
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AppSettings:ExternalApis:0:Name"] = "TransitJazzSignalR",
            ["AppSettings:ExternalApis:0:BaseUri"] = baseUri
        }).Build();
        var navigation = new StubNavigationManager();
        navigation.Initialize("https://app.example.test/#atlanta");
        return new SignalRNotificationService(
            config,
            new StubWebAssemblyHostEnvironment(),
            navigation,
            NullLogger<SignalRNotificationService>.Instance,
            new FakeLiveDeliveryConnectionFactory(connections),
            timeProvider ?? TimeProvider.System);
    }

    sealed class StubNavigationManager : NavigationManager
    {
        public void Initialize(string uri) => Initialize(uri, uri);
    }

    sealed class StubWebAssemblyHostEnvironment : IWebAssemblyHostEnvironment
    {
        public string Environment { get; set; } = "Development";
        public string ApplicationName { get; set; } = "TransitJazz.Tests";
        public string WebRootPath { get; set; } = "wwwroot";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string BaseAddress { get; set; } = "https://app.example.test/";
    }

    sealed class FakeLiveDeliveryConnectionFactory(FakeLiveDeliveryConnection[] connections)
        : ILiveDeliveryConnectionFactory
    {
        int _index;
        public ILiveDeliveryConnection Create(Uri hubUri) => connections[Math.Min(_index++, connections.Length - 1)];
    }

    sealed class FakeLiveDeliveryConnection(LiveFailureStage failureStage) : ILiveDeliveryConnection
    {
        readonly TaskCompletionSource _joinEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource _joinRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public HubConnectionState State { get; private set; } = HubConnectionState.Disconnected;
        public bool HoldJoin { get; set; }
        public bool FailNextJoin { get; set; }
        public int JoinCallCount { get; private set; }
        public int LeaveCallCount { get; private set; }
        public int StopCallCount { get; private set; }
        public int DisposeCallCount { get; private set; }
        public Task JoinEntered => _joinEntered.Task;
        public event Func<Exception?, Task>? Reconnecting;
        public event Func<string?, Task>? Reconnected;
        public event Func<Exception?, Task>? Closed;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (failureStage == LiveFailureStage.Start)
            {
                throw new InvalidOperationException("start failure sentinel");
            }

            State = HubConnectionState.Connected;
            return Task.CompletedTask;
        }

        public async Task JoinCityAsync(string city, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            JoinCallCount++;
            _joinEntered.TrySetResult();
            if (FailNextJoin)
            {
                FailNextJoin = false;
                throw new InvalidOperationException("rejoin failure sentinel");
            }
            if (failureStage == LiveFailureStage.Protocol)
            {
                throw new HubException("Method does not exist");
            }
            if (failureStage == LiveFailureStage.Join)
            {
                throw new InvalidOperationException("join failure sentinel");
            }
            if (HoldJoin)
            {
                await _joinRelease.Task.WaitAsync(cancellationToken);
            }
        }

        public Task LeaveCityAsync(string city, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LeaveCallCount++;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCallCount++;
            State = HubConnectionState.Disconnected;
            return Task.CompletedTask;
        }

        public Task RaiseReconnectingAsync(Exception? exception = null)
        {
            State = HubConnectionState.Reconnecting;
            return Reconnecting?.Invoke(exception) ?? Task.CompletedTask;
        }

        public Task RaiseReconnectedAsync(string? connectionId = null)
        {
            State = HubConnectionState.Connected;
            return Reconnected?.Invoke(connectionId) ?? Task.CompletedTask;
        }

        public Task RaiseClosedAsync(Exception? exception = null)
        {
            State = HubConnectionState.Disconnected;
            return Closed?.Invoke(exception) ?? Task.CompletedTask;
        }

        public void ReleaseJoin() => _joinRelease.TrySetResult();

        public IDisposable OnNotificationReceived(SignalRNotificationHandler handler) => new NoopDisposable();

        public ValueTask DisposeAsync()
        {
            DisposeCallCount++;
            State = HubConnectionState.Disconnected;
            return ValueTask.CompletedTask;
        }

        sealed class NoopDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    public enum LiveFailureStage
    {
        None,
        Start,
        Join,
        Protocol
    }
}
