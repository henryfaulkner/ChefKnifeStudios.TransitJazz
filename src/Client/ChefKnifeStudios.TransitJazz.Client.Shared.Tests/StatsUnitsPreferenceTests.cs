using Blazored.LocalStorage;
using Blazored.LocalStorage.Serialization;
using ChefKnifeStudios.TransitJazz.Client.Shared.Attributes;
using ChefKnifeStudios.TransitJazz.Client.Shared.Models;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class StatsUnitsPreferenceTests
{
    [Fact]
    public void ConfiguredSerializerReadsLegacySettingsAndDefaultsUnitsToMetric()
    {
        using var provider = CreateSerializerProvider();
        var serializer = provider.GetRequiredService<IJsonSerializer>();

        var settings = serializer.Deserialize<Settings>("""
            {"Version":5,"IsAudioEnabled":false,"IsDarkModeEnabled":true,"BackfillTexture":1}
            """)!;

        Assert.Equal(5, settings.Version);
        Assert.Equal(StatsUnitSystem.Metric, settings.StatsUnits);
        Assert.False(settings.IsAudioEnabled);
        Assert.True(settings.IsDarkModeEnabled);
        Assert.Equal(BackfillTexture.Percussion, settings.BackfillTexture);
    }

    [Theory]
    [InlineData(StatsUnitSystem.Metric)]
    [InlineData(StatsUnitSystem.Imperial)]
    public void ConfiguredSerializerRoundTripsUnitsWithoutChangingSettingsVersion(StatsUnitSystem units)
    {
        using var provider = CreateSerializerProvider();
        var serializer = provider.GetRequiredService<IJsonSerializer>();
        var settings = new Settings { IsAudioEnabled = false, StatsUnits = units };

        var restored = serializer.Deserialize<Settings>(serializer.Serialize(settings))!;

        Assert.Equal(Settings.CurrentVersion, restored.Version);
        Assert.Equal(units, restored.StatsUnits);
        Assert.False(restored.IsAudioEnabled);
    }

    [Fact]
    public void UndefinedStoredUnitsNormalizeToMetricWithoutResettingOtherSettings()
    {
        using var provider = CreateSerializerProvider();
        var serializer = provider.GetRequiredService<IJsonSerializer>();

        var settings = serializer.Deserialize<Settings>("""
            {"Version":5,"IsAudioEnabled":false,"IsDarkModeEnabled":true,"StatsUnits":999}
            """)!;

        Assert.Equal(StatsUnitSystem.Metric, StatsDisplayUnits.Normalize(settings.StatsUnits));
        Assert.False(settings.IsAudioEnabled);
        Assert.True(settings.IsDarkModeEnabled);
    }

    [Fact]
    public void UnitsPreferenceIsHiddenAndSettingsVersionRemainsFive()
    {
        var property = typeof(Settings).GetProperty(nameof(Settings.StatsUnits));

        Assert.NotNull(property);
        Assert.NotNull(Attribute.GetCustomAttribute(property!, typeof(HiddenSettingAttribute)));
        Assert.Equal(5, Settings.CurrentVersion);
        Assert.Equal(StatsUnitSystem.Metric, new Settings().StatsUnits);
    }

    [Fact]
    public void SettingOnePreferencePreservesOtherValuesFromLatestStoredSettings()
    {
        using var provider = CreateSerializerProvider();
        var serializer = provider.GetRequiredService<IJsonSerializer>();
        var (storage, state) = CreateStorage(serializer, new Settings { IsDarkModeEnabled = true });
        var service = new SettingsService(storage);

        state.Json = serializer.Serialize(new Settings { IsDarkModeEnabled = false, IsAudioEnabled = false });
        service.SetSettingValue(nameof(Settings.StatsUnits), StatsUnitSystem.Imperial);
        var saved = serializer.Deserialize<Settings>(state.Json!)!;

        Assert.Equal(StatsUnitSystem.Imperial, saved.StatsUnits);
        Assert.False(saved.IsDarkModeEnabled);
        Assert.False(saved.IsAudioEnabled);
        Assert.Equal(1, state.GetCalls);
        Assert.Equal(1, state.SetCalls);
    }

    [Fact]
    public void SettingsServiceStorageFailuresPropagateFromPropertyWritesAndDefaultSeeding()
    {
        using var provider = CreateSerializerProvider();
        var serializer = provider.GetRequiredService<IJsonSerializer>();
        var (storage, state) = CreateStorage(serializer, new Settings());
        var service = new SettingsService(storage);

        state.ThrowOnSet = true;
        Assert.Throws<InvalidOperationException>(() =>
            service.SetSettingValue(nameof(Settings.StatsUnits), StatsUnitSystem.Imperial));

        state.ThrowOnGet = true;
        Assert.Throws<InvalidOperationException>(() =>
            service.GetSettingValue<StatsUnitSystem>(nameof(Settings.StatsUnits)));
    }

    static ServiceProvider CreateSerializerProvider()
    {
        var services = new ServiceCollection();
        services.AddBlazoredLocalStorage();
        return services.BuildServiceProvider();
    }

    static (ISyncLocalStorageService Service, LocalStorageProxy State) CreateStorage(IJsonSerializer serializer, Settings settings)
    {
        var storage = DispatchProxy.Create<ISyncLocalStorageService, LocalStorageProxy>();
        var proxy = (LocalStorageProxy)(object)storage;
        proxy.Serializer = serializer;
        proxy.Json = serializer.Serialize(settings);
        return (storage, proxy);
    }

    public class LocalStorageProxy : DispatchProxy
    {
        public IJsonSerializer Serializer { get; set; } = default!;
        public string? Json { get; set; }
        public bool ThrowOnGet { get; set; }
        public bool ThrowOnSet { get; set; }
        public int GetCalls { get; private set; }
        public int SetCalls { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            if (targetMethod.Name == "GetItem" && targetMethod.IsGenericMethod)
            {
                if (ThrowOnGet) throw new InvalidOperationException("Injected local storage read failure.");
                GetCalls++;
                if (Json is null) return null;
                var valueType = targetMethod.GetGenericArguments()[0];
                var deserialize = typeof(IJsonSerializer).GetMethods()
                    .Single(method => method.Name == "Deserialize" && method.IsGenericMethod && method.GetParameters().Length == 1)
                    .MakeGenericMethod(valueType);
                return deserialize.Invoke(Serializer, [Json]);
            }

            if (targetMethod.Name == "SetItem" && targetMethod.IsGenericMethod)
            {
                if (ThrowOnSet) throw new InvalidOperationException("Injected local storage write failure.");
                SetCalls++;
                var valueType = targetMethod.GetGenericArguments()[0];
                var serialize = typeof(IJsonSerializer).GetMethods()
                    .Single(method => method.Name == "Serialize" && method.IsGenericMethod && method.GetParameters().Length == 1)
                    .MakeGenericMethod(valueType);
                Json = (string?)serialize.Invoke(Serializer, [args![1]]);
                return null;
            }

            if (targetMethod.ReturnType == typeof(void)) return null;
            return targetMethod.ReturnType.IsValueType ? Activator.CreateInstance(targetMethod.ReturnType) : null;
        }
    }
}
