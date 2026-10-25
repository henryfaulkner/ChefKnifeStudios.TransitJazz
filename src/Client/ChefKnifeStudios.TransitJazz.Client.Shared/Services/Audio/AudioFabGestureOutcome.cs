namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services.Audio;

/// <summary>Bounded result for one native click on the audio FAB.</summary>
public sealed record AudioFabGestureOutcome(bool WasRunning, string Status);
