using ChefKnifeStudios.TransitJazz.Client.Shared.Attributes;
using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Models;

public enum BackfillTexture
{
    Noise,      // default — today's continuous pink-noise bed
    Percussion, // sparse lo-fi kit on a slow Tone.Transport loop
}

public partial class Settings : ObservableObject
{
    // ponytail: bump CurrentVersion when schema changes, old serialized data auto-discards
    public const int CurrentVersion = 5;
    [HiddenSetting]
    public int Version { get; set; } = CurrentVersion;

    [ObservableProperty]
    [property: Description("SettingAudioEnabled")]
    bool _isAudioEnabled = true;

    [ObservableProperty]
    [property: Description("SettingCheckpointsVisible")]
    bool _areCheckpointsVisible = true;

    [ObservableProperty]
    [property: Description("SettingCrossingTrailVisible")]
    bool _isCrossingTrailVisible = false;

    [ObservableProperty]
    [property: Description("SettingStreetMap")]
    bool _isStreetMapEnabled = true;

    [ObservableProperty]
    [property: Description("SettingBusesVisible")]
    bool _isBusesVisible = false;

    [ObservableProperty]
    [property: Description("SettingAllCheckpointsVisible")]
    bool _areAllCheckpointsVisible = false;

    [ObservableProperty]
    [property: Description("SettingDarkMode")]
    bool _isDarkModeEnabled = false;

    [ObservableProperty]
    [property: HiddenSetting]
    BackfillTexture _backfillTexture = BackfillTexture.Noise;
}
