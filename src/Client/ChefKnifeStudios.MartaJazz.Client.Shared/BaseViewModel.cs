using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;

namespace ChefKnifeStudios.MartaJazz.Client.Shared;

public interface IViewModel : INotifyPropertyChanged
{
}

// Base class using source generators
public partial class BaseViewModel : ObservableObject, IViewModel
{
    // Source generators automatically implement INotifyPropertyChanged
}
