using System.Reflection;
using Blazored.LocalStorage;
using ChefKnifeStudios.TransitJazz.Client.Shared.Models;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class SettingsSnapshotTests
{
    [Fact]
    public void MissingSettingsSeedDefaults()
    {
        var (localStorage, storage) = CreateStorage();
        var service = new SettingsService(localStorage);

        var settings = service.GetSettings();

        Assert.Equal(Settings.CurrentVersion, settings.Version);
        Assert.True(settings.IsAudioEnabled);
        Assert.Same(settings, storage.StoredValue);
        Assert.Equal(1, storage.WriteCount);
    }

    [Fact]
    public void VersionChangedSettingsResetToCurrentDefaults()
    {
        var (localStorage, storage) = CreateStorage();
        storage.StoredValue = new Settings { Version = Settings.CurrentVersion - 1, IsAudioEnabled = false };
        var service = new SettingsService(localStorage);

        var settings = service.GetSettings();

        Assert.Equal(Settings.CurrentVersion, settings.Version);
        Assert.True(settings.IsAudioEnabled);
        Assert.Same(settings, storage.StoredValue);
        Assert.Equal(1, storage.WriteCount);
    }

    [Fact]
    public void CorruptSettingsResetToCurrentDefaults()
    {
        var (localStorage, storage) = CreateStorage();
        storage.StoredValue = CorruptValue.Instance;
        var service = new SettingsService(localStorage);

        var settings = service.GetSettings();

        Assert.Equal(Settings.CurrentVersion, settings.Version);
        Assert.True(settings.IsAudioEnabled);
        Assert.Same(settings, storage.StoredValue);
        Assert.Equal(1, storage.WriteCount);
    }

    [Fact]
    public void FailedInitialDefaultWriteStaysSilentAndReturnsDefaults()
    {
        var (localStorage, storage) = CreateStorage();
        storage.ReadFailure = new InvalidOperationException("storage is blocked");
        storage.WriteFailure = new InvalidOperationException("storage is blocked");
        var service = new SettingsService(localStorage);

        var settings = service.GetSettings();

        Assert.Equal(Settings.CurrentVersion, settings.Version);
        Assert.True(settings.IsAudioEnabled);
    }

    [Fact]
    public void AllReadersShareTheSameSnapshotWhenStorageIsBlocked()
    {
        var (localStorage, storage) = CreateStorage();
        storage.ReadFailure = new InvalidOperationException("storage is blocked");
        storage.WriteFailure = new InvalidOperationException("storage is blocked");
        var service = new SettingsService(localStorage);

        var firstReader = service.GetSettings();
        firstReader.IsAudioEnabled = false;
        Assert.Throws<InvalidOperationException>(() => service.SaveSettings(firstReader));
        var secondReader = service.GetSettings();

        Assert.Same(firstReader, secondReader);
        Assert.False(secondReader.IsAudioEnabled);
        Assert.False(service.GetSettingValue<bool>(nameof(Settings.IsAudioEnabled)));
    }

    [Fact]
    public void StorageRecoveryDoesNotReplaceTheCurrentSessionSnapshot()
    {
        var (localStorage, storage) = CreateStorage();
        storage.ReadFailure = new InvalidOperationException("storage is blocked");
        storage.WriteFailure = new InvalidOperationException("storage is blocked");
        var service = new SettingsService(localStorage);
        var current = service.GetSettings();
        current.IsAudioEnabled = false;

        storage.ReadFailure = null;
        storage.WriteFailure = null;
        storage.StoredValue = new Settings { IsAudioEnabled = true };

        var afterStorageRecovery = service.GetSettings();

        Assert.Same(current, afterStorageRecovery);
        Assert.False(afterStorageRecovery.IsAudioEnabled);
    }

    static (ISyncLocalStorageService Service, SyncStorageProxy State) CreateStorage()
    {
        var service = DispatchProxy.Create<ISyncLocalStorageService, SyncStorageProxy>();
        return (service, (SyncStorageProxy)service);
    }

    sealed class CorruptValue
    {
        public static readonly CorruptValue Instance = new();
    }

    public class SyncStorageProxy : DispatchProxy
    {
        public object? StoredValue { get; set; }
        public Exception? ReadFailure { get; set; }
        public Exception? WriteFailure { get; set; }
        public int WriteCount { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Assert.NotNull(targetMethod);
            var method = targetMethod!;
            switch (method.Name)
            {
                case "GetItem":
                    if (ReadFailure is not null) throw ReadFailure;
                    if (StoredValue is CorruptValue) throw new InvalidOperationException("stored JSON is corrupt");
                    return StoredValue;
                case "SetItem":
                    if (WriteFailure is not null) throw WriteFailure;
                    StoredValue = args![1];
                    WriteCount++;
                    return null;
                default:
                    throw new NotSupportedException($"Unexpected storage call: {method.Name}");
            }
        }
    }
}
