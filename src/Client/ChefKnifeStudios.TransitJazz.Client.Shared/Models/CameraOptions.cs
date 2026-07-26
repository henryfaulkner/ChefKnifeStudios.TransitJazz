using System;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Models;

public record CameraOptions
{
    const double MIN_ZOOM = 1;
    const double MAX_ZOOM = 24;

    private double _zoom;
    public required Position Center { get; init; }

    public required double Zoom
    {
        get => _zoom;
        set
        {
            if (value is >= MIN_ZOOM and <= MAX_ZOOM)
                _zoom = value;
            else
                throw new ArgumentOutOfRangeException(nameof(Zoom));
        }
    }

}