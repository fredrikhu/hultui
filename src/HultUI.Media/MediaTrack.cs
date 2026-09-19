namespace HultUI.Media;

/// <summary>An audio or subtitle track a backend found in the open media.</summary>
/// <param name="Id">The backend's identifier, passed back to select the track.</param>
/// <param name="Title">What to show; the language name when the track has no title.</param>
/// <param name="Language">An ISO 639 code when the media says, otherwise null.</param>
public sealed record MediaTrack(int Id, string Title, string? Language = null);
