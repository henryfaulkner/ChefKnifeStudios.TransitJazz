using ChefKnifeStudios.MartaJazz.Client.Shared.Data;
using ChefKnifeStudios.MartaJazz.Client.Shared.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using System;
using System.ComponentModel;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.Components;

public partial class RouteBlurbBar : ComponentBase, IDisposable
{
    [Inject] IRouteFilterViewModel RouteFilterViewModel { get; set; } = null!;
    [Inject] IRouteBlurbStore RouteBlurbStore { get; set; } = null!;
    [Inject] IStringLocalizer<RouteFilterResources> _localizer { get; set; } = null!;

    RouteBlurb? _blurb;

    protected override void OnInitialized()
    {
        RouteFilterViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public void Dispose()
    {
        RouteFilterViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IRouteFilterViewModel.RouteItems) or nameof(IRouteFilterViewModel.HasSelection))
        {
            _blurb = RouteFilterViewModel.SelectedRouteId is { } id
                ? RouteBlurbStore.GetForRoute(id)
                : null;
            InvokeAsync(StateHasChanged);
        }
    }
}
