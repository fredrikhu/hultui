using HultUI.Media;

namespace Aperture;

/// <summary>
/// Which audio and subtitle track a film opens with.
///
/// <para>The rule and the state machine are <see cref="TrackPreference"/>'s, in the media
/// layer, because Reel wants exactly the same behaviour for a file on disk and the reason
/// for it — a viewer who switched to the English dub means it for the next episode — has
/// nothing to do with where the video came from. What stays here is the only part that is
/// Aperture's: that the preference lives in Aperture's settings file.</para>
/// </summary>
public sealed partial class ApertureApp
{
    private TrackPreference? _trackPreference;

    /// <summary>The preference, seeded from settings on first use and written back whenever
    /// the viewer changes a track.</summary>
    private TrackPreference Tracks
    {
        get
        {
            if (_trackPreference is not null) return _trackPreference;

            var preference = new TrackPreference
            {
                AudioLanguage = _settings.Data.PreferredAudioLanguage,
                SubtitleLanguage = _settings.Data.PreferredSubtitleLanguage,
                SubtitlesOn = _settings.Data.SubtitlesOn,
            };
            preference.Changed += () =>
            {
                _settings.Data.PreferredAudioLanguage = preference.AudioLanguage;
                _settings.Data.PreferredSubtitleLanguage = preference.SubtitleLanguage;
                _settings.Data.SubtitlesOn = preference.SubtitlesOn;
                _settings.Save();
            };
            return _trackPreference = preference;
        }
    }

    private void ForgetTrackSelection() => Tracks.Forget();

    private void PumpTrackPreference(MediaPlayer player) => Tracks.Pump(player);
}
