using ChefKnifeStudios.MartaJazz.Client.Shared.Attributes;
using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.Models;

public partial class Settings : ObservableObject
{
    [ObservableProperty]
    [property: Description("SettingAudioEnabled")]
    bool _isAudioEnabled = true;

    [ObservableProperty]
    [property: Description("SettingCheckpointsVisible")]
    [property: HiddenSetting]
    bool _areCheckpointsVisible = true;

    [ObservableProperty]
    [property: Description("SettingStreetMap")]
    bool _isStreetMapEnabled = false;

    [ObservableProperty]
    [property: Description("SettingBusesVisible")]
    [property: HiddenSetting]
    bool _isBusesVisible = false;

    [ObservableProperty]
    [property: Description("SettingAllCheckpointsVisible")]
    [property: HiddenSetting]
    bool _areAllCheckpointsVisible = false;
}
