using Blazored.LocalStorage;
using ChefKnifeStudios.TransitJazz.Client.Shared.Constants;
using ChefKnifeStudios.TransitJazz.Client.Shared.Models;
using System;
using System.Reflection;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services;

public interface ISettingsService
{
    Settings GetSettings();
    void SaveSettings(Settings settings);
    T? GetSettingValue<T>(string propertyName);
    void SetSettingValue<T>(string propertyName, T value);
}

public class SettingsService : ISettingsService
{
    readonly ISyncLocalStorageService _localStorage;
    Settings? _snapshot;

    public SettingsService(ISyncLocalStorageService localStorage)
    {
        _localStorage = localStorage;
    }

    public Settings GetSettings()
    {
        if (_snapshot is not null) return _snapshot;

        try
        {
            var settings = _localStorage.GetItem<Settings>(LocalStorageConstants.SettingsKey);
            if (settings is not null && settings.Version == Settings.CurrentVersion)
            {
                _snapshot = settings;
                return settings;
            }
        }
        catch
        {
            // corrupt/unparseable — fall through to seed defaults
        }

        _snapshot = new Settings();
        try
        {
            _localStorage.SetItem(LocalStorageConstants.SettingsKey, _snapshot);
        }
        catch
        {
            // Keep startup defaults in memory if browser storage is unavailable.
        }

        return _snapshot;
    }

    public void SaveSettings(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _snapshot = settings;
        _localStorage.SetItem(LocalStorageConstants.SettingsKey, settings);
    }

    public T? GetSettingValue<T>(string propertyName)
    {
        var settings = GetSettings();
        var prop = typeof(Settings).GetProperty(propertyName);
        if (prop is null) return default;
        var value = prop.GetValue(settings);
        if (value is T typed) return typed;
        return default;
    }

    public void SetSettingValue<T>(string propertyName, T value)
    {
        var settings = GetSettings();
        var prop = typeof(Settings).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (prop is null) return;
        prop.SetValue(settings, value);
        SaveSettings(settings);
    }
}
