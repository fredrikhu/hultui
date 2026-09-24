using HultUI.Media;

namespace Reel;

/// <summary>
/// Which audio and subtitle track a file opens with, across files.
///
/// <para>The rule and the state machine are <see cref="TrackPreference"/>'s, shared with
/// Aperture: a viewer who switched to the English dub, or closed the subtitles, means it for
/// the next episode too, and that is true whether the episode came off a server or off a
/// disk. What is Reel's is only where the preference is kept.</para>
///
/// <para>The track menus themselves are the media layer's — they are part of the transport
/// overlay both apps mount — so nothing here draws anything. This watches what they did.</para>
/// </summary>
public sealed partial class ReelApp
{
    private TrackPreference? _trackPreference;

    /// <summary>The preference, seeded from settings on first use and written back whenever
    /// the viewer changes a track.</summary>
    internal TrackPreference Tracks
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
}
