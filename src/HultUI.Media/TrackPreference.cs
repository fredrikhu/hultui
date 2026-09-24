namespace HultUI.Media;

/// <summary>
/// Which audio and subtitle track a file opens with, remembered across files.
///
/// <para>A backend picks the file's own default, which is right the first time and wrong
/// every time after: someone who switched to the English dub, or turned subtitles off,
/// means it for the next file too. So the choice is remembered by <i>language</i> — not by
/// track number, which means nothing across files — and re-applied to each new file that
/// has a track matching it.</para>
///
/// <para>The remembering is deliberately one-way: what <see cref="Pump"/> applies never
/// overwrites the preference, only a person changing a track does. Otherwise a file with no
/// English audio would quietly rewrite the preference to whatever it happened to have.</para>
///
/// <para>Persistence belongs to the caller. The three properties are the whole state;
/// <see cref="Changed"/> says when to write them somewhere.</para>
/// </summary>
public sealed class TrackPreference
{
    /// <summary>The language tag (or track title) to look for in a file's audio tracks.</summary>
    public string? AudioLanguage { get; set; }

    /// <summary>The same for subtitles, consulted only when <see cref="SubtitlesOn"/>.</summary>
    public string? SubtitleLanguage { get; set; }

    /// <summary>Whether subtitles are wanted at all. Turning them off is itself a
    /// preference: a viewer who closed them means it for the next file too.</summary>
    public bool SubtitlesOn { get; set; } = true;

    /// <summary>Raised after a viewer's own track change has updated the properties above.
    /// Never raised for a change this class made.</summary>
    public event Action? Changed;

    /// <summary>Tracks as they were when the preference was last applied, so the next
    /// change is recognised as a change rather than as the application itself.</summary>
    private int? _appliedAudioTrack;
    private int? _appliedSubtitleTrack;

    /// <summary>True once the preference has been applied to the file that is open.</summary>
    private bool _applied;

    /// <summary>Call when a new file is opened: the next <see cref="Pump"/> applies the
    /// preference again rather than reading the new file's defaults as a viewer's choice.</summary>
    public void Forget()
    {
        _applied = false;
        _appliedAudioTrack = null;
        _appliedSubtitleTrack = null;
    }

    /// <summary>
    /// Call from the frame while a file is open. Waits for the backend to report the file's
    /// tracks — they arrive after the file loads, not with it — then applies the remembered
    /// languages, and afterwards watches for the viewer changing one.
    /// </summary>
    public void Pump(MediaPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (!_applied)
        {
            // No tracks yet means the file is still loading. A file with no audio at all
            // (a silent clip) would wait forever, so the presence of *either* list is
            // taken as "the backend has told us what it has".
            if (player.AudioTracks.Count == 0 && player.SubtitleTracks.Count == 0) return;
            Apply(player);
            _applied = true;
            _appliedAudioTrack = player.SelectedAudioTrack;
            _appliedSubtitleTrack = player.SelectedSubtitleTrack;
            return;
        }

        // From here on, any difference is the viewer's doing — the track menus in the
        // overlay write straight to the player, so this is where that shows up.
        bool changed = false;

        if (player.SelectedAudioTrack != _appliedAudioTrack)
        {
            _appliedAudioTrack = player.SelectedAudioTrack;
            var track = player.AudioTracks.FirstOrDefault(t => t.Id == player.SelectedAudioTrack);
            if (LanguageOf(track) is { } language)
            {
                AudioLanguage = language;
                changed = true;
            }
        }

        if (player.SelectedSubtitleTrack != _appliedSubtitleTrack)
        {
            _appliedSubtitleTrack = player.SelectedSubtitleTrack;
            if (player.SelectedSubtitleTrack is null)
            {
                SubtitlesOn = false;
            }
            else
            {
                var track = player.SubtitleTracks.FirstOrDefault(t => t.Id == player.SelectedSubtitleTrack);
                SubtitlesOn = true;
                if (LanguageOf(track) is { } language) SubtitleLanguage = language;
            }
            changed = true;
        }

        if (changed) Changed?.Invoke();
    }

    private void Apply(MediaPlayer player)
    {
        if (Match(player.AudioTracks, AudioLanguage) is { } audio)
            player.SelectAudioTrack(audio.Id);

        if (!SubtitlesOn)
        {
            // Only turn them off if the file turned them on: a file that opens without
            // subtitles needs no instruction, and issuing one would count as a change.
            if (player.SelectedSubtitleTrack is not null) player.SelectSubtitleTrack(null);
        }
        else if (Match(player.SubtitleTracks, SubtitleLanguage) is { } subtitle)
        {
            player.SelectSubtitleTrack(subtitle.Id);
        }
    }

    /// <summary>
    /// The track whose language matches, or null. Matching is on the language tag when the
    /// file has one and on the title otherwise, because that is all some files carry —
    /// "English (SDH)" and "eng" both have to find their way back to an English track.
    /// </summary>
    public static MediaTrack? Match(IReadOnlyList<MediaTrack> tracks, string? language)
    {
        ArgumentNullException.ThrowIfNull(tracks);
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
