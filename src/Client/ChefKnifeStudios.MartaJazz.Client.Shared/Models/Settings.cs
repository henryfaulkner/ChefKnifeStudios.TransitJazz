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
    bool _areCheckpointsVisible = true;

    [ObservableProperty]
    [property: Description("SettingCrossingTrailVisible")]
    bool _isCrossingTrailVisible = true;

    [ObservableProperty]
    [property: Description("SettingStreetMap")]
    bool _isStreetMapEnabled = false;

    [ObservableProperty]
    [property: Description("SettingBusesVisible")]
    bool _isBusesVisible = false;

    [ObservableProperty]
    [property: Description("SettingAllCheckpointsVisible")]
    bool _areAllCheckpointsVisible = false;
}
