using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.Models;

public partial class Settings : ObservableObject
{
    [ObservableProperty]
    [property: Description("SettingAudioEnabled")]
    private bool _isAudioEnabled = true;

    [ObservableProperty]
    [property: Description("SettingCheckpointsVisible")]
    private bool _areCheckpointsVisible = true;

    [ObservableProperty]
    [property: Description("SettingStreetMap")]
    private bool _isStreetMapEnabled = false;
}
