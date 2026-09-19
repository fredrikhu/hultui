namespace HultUI.Media;

/// <summary>Where playback stands. Reported by the backend; the player never guesses.</summary>
public enum PlaybackState
{
    /// <summary>Nothing open.</summary>
    NoMedia,
    /// <summary>A source is being opened; duration and size are not known yet.</summary>
    Opening,
    Playing,
    Paused,
    /// <summary>Playback reached the end. <c>Play</c> starts over from the beginning.</summary>
    Ended,
    /// <summary>The source could not be opened or played; see <see cref="MediaPlayer.Error"/>.</summary>
    Failed,
}
