using ChefKnifeStudios.MartaJazz.Client.Shared.Attributes;
using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.Models;

public partial class Settings : ObservableObject
{
    // ponytail: bump CurrentVersion when schema changes, old serialized data auto-discards
    public const int CurrentVersion = 3;
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
    bool _isStreetMapEnabled = false;

    [ObservableProperty]
    [property: Description("SettingBusesVisible")]
    bool _isBusesVisible = false;

    [ObservableProperty]
    [property: Description("SettingAllCheckpointsVisible")]
    bool _areAllCheckpointsVisible = false;

    [ObservableProperty]
    [property: Description("SettingDarkMode")]
    bool _isDarkModeEnabled = false;
}
