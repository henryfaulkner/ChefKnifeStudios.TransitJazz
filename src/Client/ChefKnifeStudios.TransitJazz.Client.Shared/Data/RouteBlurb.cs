namespace ChefKnifeStudios.TransitJazz.Client.Shared.Data;

public sealed record RouteBlurb(
    string RouteId,
    string ToneDescription,
    string Significance,
    bool IsPlaceholder = false);
