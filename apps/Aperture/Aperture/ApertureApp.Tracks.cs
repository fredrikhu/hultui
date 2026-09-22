using HultUI.Media;

namespace Aperture;

/// <summary>
/// Which audio and subtitle track a file opens with.
///
/// <para>mpv picks the file's own default, which is right the first time and wrong every
/// time after: someone who switched to the English dub, or turned subtitles off, means it
/// for the episode after this one too. So the choice is remembered by <i>language</i> —
/// not by track number, which means nothing across files — and re-applied to each new
/// file that has a track matching it.</para>
///
/// <para>The remembering is deliberately one-way: what is applied here never overwrites
/// the preference, only a person changing a track does. Otherwise a file with no English
/// audio would quietly rewrite the preference to whatever it happened to have.</para>
/// </summary>
public sealed partial class ApertureApp
{
    /// <summary>Tracks as they were when the preference was last applied, so the next
    /// change is recognised as a change rather than as the application itself.</summary>
    private int? _appliedAudioTrack;
    private int? _appliedSubtitleTrack;

    /// <summary>True once the preference has been applied to the file that is open; reset
    /// by each new stream.</summary>
    private bool _tracksApplied;

    private void ForgetTrackSelection()
    {
        _tracksApplied = false;
        _appliedAudioTrack = null;
        _appliedSubtitleTrack = null;
    }

    /// <summary>
    /// Called from the playback pump. Waits for the backend to report the file's tracks —
    /// they arrive after the file loads, not with it — then applies the remembered
    /// languages, and afterwards watches for the viewer changing one.
    /// </summary>
    private void PumpTrackPreference(MediaPlayer player)
    {
        if (!_tracksApplied)
        {
            // No tracks yet means the file is still loading. A file with no audio at all
            // (a silent clip) would wait forever, so the presence of *either* list is
            // taken as "the backend has told us what it has".
            if (player.AudioTracks.Count == 0 && player.SubtitleTracks.Count == 0) return;
            ApplyTrackPreference(player);
            _tracksApplied = true;
            _appliedAudioTrack = player.SelectedAudioTrack;
            _appliedSubtitleTrack = player.SelectedSubtitleTrack;
            return;
        }

        // From here on, any difference is the viewer's doing — the track menus in the
        // overlay write straight to the player, so this is where that shows up.
        if (player.SelectedAudioTrack != _appliedAudioTrack)
        {
            _appliedAudioTrack = player.SelectedAudioTrack;
            var track = player.AudioTracks.FirstOrDefault(t => t.Id == player.SelectedAudioTrack);
            if (LanguageOf(track) is { } language)
            {
                _settings.Data.PreferredAudioLanguage = language;
                _settings.Save();
            }
        }

        if (player.SelectedSubtitleTrack != _appliedSubtitleTrack)
        {
            _appliedSubtitleTrack = player.SelectedSubtitleTrack;
            if (player.SelectedSubtitleTrack is null)
            {
                _settings.Data.SubtitlesOn = false;
            }
            else
            {
                var track = player.SubtitleTracks.FirstOrDefault(t => t.Id == player.SelectedSubtitleTrack);
                _settings.Data.SubtitlesOn = true;
                if (LanguageOf(track) is { } language) _settings.Data.PreferredSubtitleLanguage = language;
            }
            _settings.Save();
        }
    }

    private void ApplyTrackPreference(MediaPlayer player)
    {
        if (Match(player.AudioTracks, _settings.Data.PreferredAudioLanguage) is { } audio)
            player.SelectAudioTrack(audio.Id);

        if (!_settings.Data.SubtitlesOn)
        {
            // Only turn them off if the file turned them on: a file that opens without
            // subtitles needs no instruction, and issuing one would count as a change.
            if (player.SelectedSubtitleTrack is not null) player.SelectSubtitleTrack(null);
        }
        else if (Match(player.SubtitleTracks, _settings.Data.PreferredSubtitleLanguage) is { } subtitle)
        {
            player.SelectSubtitleTrack(subtitle.Id);
        }
    }

    /// <summary>
    /// The track whose language matches, or null. Matching is on the language tag when the
    /// file has one and on the title otherwise, because that is all some files carry —
    /// "English (SDH)" and "eng" both have to find their way back to an English track.
    /// </summary>
    internal static MediaTrack? Match(IReadOnlyList<MediaTrack> tracks, string? language)
    {
        if (string.IsNullOrWhiteSpace(language) || tracks.Count == 0) return null;

        var exact = tracks.FirstOrDefault(t =>
            string.Equals(t.Language, language, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        return tracks.FirstOrDefault(t =>
            t.Title.Contains(language, StringComparison.OrdinalIgnoreCase) ||
            (t.Language is { } l && language.StartsWith(l, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>What to remember a track by: its language tag, or its title when the file
    /// gave it none.</summary>
    private static string? LanguageOf(MediaTrack? track) =>
        track is null ? null
        : !string.IsNullOrWhiteSpace(track.Language) ? track.Language
        : !string.IsNullOrWhiteSpace(track.Title) ? track.Title
        : null;
}
