using System.Reflection;
using HultUI.Core.Events;
using HultUI.Core.Builders;
using HultUI.Core.Platform;
using HultUI.Headless;
using HultUI.Media;
using HultUI.Media.Testing;
using Reel;
using Xunit;

namespace Reel.Tests;

/// <summary>
/// Reel hosted headless over a fake decoder: no window, no libmpv, no real files beyond
/// the empty ones these create. What is pinned here is the part that is Reel's rather than
/// the media layer's — the folder as a running order, where a file was left, and what the
/// window says when there is nothing to play.
/// </summary>
public class ReelTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "reel-tests", Guid.NewGuid().ToString("N"));

    public ReelTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    /// <summary>Creates an empty file of the given name and returns its path. Nothing here
    /// decodes anything, so the contents never matter.</summary>
    private string File_(string name)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, new byte[64]);
        return path;
    }

    private (HeadlessSession Session, ReelApp App, FakeMediaBackend Backend) Host(ReelSettings? settings = null)
    {
        var session = HeadlessSession.Create(900, 600);
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(24) };
        var app = new ReelApp(session.Application, session.Window.Window,
            settings ?? new ReelSettings(Path.Combine(_folder, "settings.json")),
            () => backend);
        session.Application.SetRoot(app.Mount());
        session.PumpFrame();
        return (session, app, backend);
    }

    // ── The folder as a running order ────────────────────────────────────

    [Fact]
    public void OpeningOneEpisodeMakesTheFolderTheRunningOrder()
    {
        // Named as releases are, so the ordering has something to get wrong.
        File_("Show - 01.mkv");
        File_("Show - 02.mkv");
        var tenth = File_("Show - 10.mkv");
        File_("cover.jpg");                       // not playable, not in the order

        var (session, app, _) = Host();
        using var _s = session;
        app.Open(tenth);
        session.PumpFrames(2);

        Assert.Equal(3, app.Playlist!.Count);
        Assert.Equal(2, app.Playlist.Index);       // 01, 02, 10 — and 10 is last, not second
        Assert.Equal("3 of 3", app.Playlist.Position);
        Assert.EndsWith("Show - 10.mkv", app.CurrentFile);
    }

    [Fact]
    public void NextAndPreviousWalkTheFolderAndStopAtTheEnds()
    {
        var first = File_("a.mkv");
        File_("b.mkv");

        var (session, app, _) = Host();
        using var _s = session;
        app.Open(first);
        session.PumpFrames(2);

        Assert.True(app.Step(1));
        Assert.EndsWith("b.mkv", app.CurrentFile);
        // The end of a folder is the end of the evening, not a wrap to the first again.
        Assert.False(app.Step(1));
        Assert.EndsWith("b.mkv", app.CurrentFile);

        Assert.True(app.Step(-1));
        Assert.EndsWith("a.mkv", app.CurrentFile);
        Assert.False(app.Step(-1));
    }

    [Fact]
    public void AFileOnItsOwnIsAPlaylistOfOne()
    {
        var only = File_("lonely.mkv");

        var (session, app, _) = Host();
        using var _s = session;
        app.Open(only);
        session.PumpFrames(2);

        Assert.Equal(1, app.Playlist!.Count);
        Assert.Equal("", app.Playlist.Position);   // no "1 of 1" in the title
        Assert.False(app.Step(1));
    }

    // ── Where it was left ────────────────────────────────────────────────

    [Fact]
    public void TheWindowPlacementSurvivesARestart()
    {
        // Through the app's own source-generated context, which publishes AOT.
        var placement = new HultUI.Core.Platform.WindowPlacement(120, 80, 1280, 760, true);
        var settings = new ReelSettings(Path.Combine(_folder, "settings.json"));
        settings.Data.WindowPlacement = placement;
        settings.Save();

        Assert.Equal(placement, new ReelSettings(Path.Combine(_folder, "settings.json")).Data.WindowPlacement);
    }

    [Fact]
    public void AFileResumesWhereItWasLeftAndAFinishedOneDoesNot()
    {
        var path = File_("film.mkv");
        var settings = new ReelSettings(Path.Combine(_folder, "settings.json"));

        settings.Remember(path, TimeSpan.FromMinutes(12), TimeSpan.FromMinutes(24));
        Assert.Equal(TimeSpan.FromMinutes(12), settings.ResumeFor(path));

        // Watched to the end: nothing to resume, or every finished film reopens in its
        // closing credits.
        settings.Remember(path, TimeSpan.FromMinutes(23.9), TimeSpan.FromMinutes(24));
        Assert.Null(settings.ResumeFor(path));

        // Barely started is not started.
        settings.Remember(path, TimeSpan.FromSeconds(4), TimeSpan.FromMinutes(24));
        Assert.Null(settings.ResumeFor(path));
    }

    [Fact]
    public void APathThatNowHoldsADifferentFileDoesNotResume()
    {
        var path = File_("episode.mkv");
        var settings = new ReelSettings(Path.Combine(_folder, "settings.json"));
        settings.Remember(path, TimeSpan.FromMinutes(12), TimeSpan.FromMinutes(24));

        // Same name, different encode: the size says so.
        File.WriteAllBytes(path, new byte[4096]);

        Assert.Null(settings.ResumeFor(path));
    }

    [Fact]
    public void PlayingSeeksToTheRememberedPointOnceTheFileHasLoaded()
    {
        var path = File_("film.mkv");
        var settings = new ReelSettings(Path.Combine(_folder, "settings.json"));
        settings.Remember(path, TimeSpan.FromMinutes(12), TimeSpan.FromMinutes(24));

        var (session, app, _) = Host(settings);
        using var _s = session;
        app.Open(path);
        // mpv cannot open at a position, so the seek waits for the load and then happens.
        session.PumpFrames(4);

        Assert.Equal(TimeSpan.FromMinutes(12), app.Player!.Position);
    }

    [Fact]
    public void OpeningWithoutResumeStartsAtTheBeginning()
    {
        var path = File_("film.mkv");
        var settings = new ReelSettings(Path.Combine(_folder, "settings.json"));
        settings.Remember(path, TimeSpan.FromMinutes(12), TimeSpan.FromMinutes(24));

        var (session, app, _) = Host(settings);
        using var _s = session;
        app.Open(path, resume: false);
        session.PumpFrames(4);

        Assert.Equal(TimeSpan.Zero, app.Player!.Position);
    }

    // ── While it plays ───────────────────────────────────────────────────

    /// <summary>
    /// Plays under the application's own loop (<see cref="HeadlessSession.RunLoop"/>) for
    /// <paramref name="seconds"/> of virtual time with nothing touched: the backend posts its
    /// position every 100 ms, as mpv does, with the frames that fall due. <paramref name="eachPass"/>
    /// runs once per pass of the loop.
    /// </summary>
    private static void PlayQuietly(HeadlessSession session, FakeMediaBackend backend, double seconds,
        Action? eachPass = null)
    {
        long end = session.Time.GetTimestamp() + TimeSpan.FromSeconds(seconds).Ticks;
        session.RunLoop(() =>
            {
                eachPass?.Invoke();
                return session.Time.GetTimestamp() >= end;
            },
            eventEvery: TimeSpan.FromMilliseconds(100),
            onEvent: () => HultUI.Application.InvokeOnMainThread(() => backend.Advance(TimeSpan.FromMilliseconds(100))),
            limit: TimeSpan.FromSeconds(seconds + 5));
    }

    /// <summary>
    /// The resume point is written every fifteen seconds of playing, so a crash or a power cut
    /// costs seconds rather than the evening. It was written from the app's build, which during
    /// quiet playback hardly runs (the transport bar is a component of its own): from Reel's
    /// first version until 2026-10-11 a film that simply played was saved only when it was
    /// closed, changed or finished.
    /// </summary>
    [Fact]
    public void WhilePlaying_TheResumePointIsWrittenEveryFifteenSeconds_WithoutRebuildingTheApp()
    {
        var path = File_("film.mkv");
        var settings = new ReelSettings(Path.Combine(_folder, "settings.json"));
        settings.Remember(path, TimeSpan.FromMinutes(12), TimeSpan.FromMinutes(24));

        var (session, app, backend) = Host(settings);
        using var _s = session;
        app.Open(path);
        session.PumpFrames(4);
        PlayQuietly(session, backend, 4);                 // the bar has gone
        int builds = app.Builds;

        var saves = new List<(double At, double Position)>();
        double? last = settings.Data.Resume.Single().Position;
        long start = session.Time.GetTimestamp();
        PlayQuietly(session, backend, 61, () =>
        {
            double now = settings.Data.Resume.Single().Position;
            if (now == last) return;
            last = now;
            saves.Add((session.Time.GetElapsedTime(start).TotalSeconds, now));
        });

        // 4 s into the film when the minute began: saves at 15, 30, 45 and 60 s of playing.
        Assert.Equal(4, saves.Count);
        for (int i = 1; i < saves.Count; i++)
        {
            Assert.InRange(saves[i].At - saves[i - 1].At, 15.0, 15.2);
            Assert.InRange(saves[i].Position - saves[i - 1].Position, 15.0, 15.2);
        }
        Assert.InRange(settings.ResumeFor(path)!.Value.TotalSeconds, 12 * 60 + 59, 12 * 60 + 61);
        Assert.Equal(builds, app.Builds);
    }

    /// <summary>
    /// mpv loads a file after it opens: the length, the tracks and the chapter marks arrive a
    /// moment later, with nothing else happening. Everything that waits for them happens then —
    /// the resume seek, the remembered audio language, the marks on the scrubber.
    /// </summary>
    [Fact]
    public void WhatWaitsForTheLoadHappensWhenTheLoadComes()
    {
        var path = File_("film.mkv");
        var settings = new ReelSettings(Path.Combine(_folder, "settings.json"));
        settings.Remember(path, TimeSpan.FromMinutes(12), TimeSpan.FromMinutes(24));
        settings.Data.PreferredAudioLanguage = "eng";

        var (session, app, backend) = Host(settings);
        using var _s = session;
        backend.HoldOpening = true;
        backend.AudioTracks.Add(new MediaTrack(1, "Japanese", "jpn"));
        backend.AudioTracks.Add(new MediaTrack(2, "English", "eng"));
        Chapters(backend, 0, 10, 20);

        app.Open(path);
        session.PumpFrames(4);
        Assert.Equal(PlaybackState.Opening, app.Player!.State);
        Assert.DoesNotContain(backend.Calls, c => c.StartsWith("Seek(", StringComparison.Ordinal));

        backend.FinishOpening();
        session.PumpFrames(2);

        Assert.Equal(PlaybackState.Playing, app.Player.State);
        Assert.Equal(TimeSpan.FromMinutes(12), app.Player.Position);
        Assert.Equal(2, app.Player.SelectedAudioTrack);
        Assert.Equal(3, app.VideoPlayer!.ChapterMarks.Count);
    }

    /// <summary>The end of a file is saved as finished (no resume point: it would reopen in its
    /// closing credits) and the folder rolls on to the next one, with nobody pressing anything.</summary>
    [Fact]
    public void AtTheEndOfAFileItIsForgottenAndTheNextOnePlays()
    {
        var first = File_("Show - 01.mkv");
        File_("Show - 02.mkv");
        var settings = new ReelSettings(Path.Combine(_folder, "settings.json"));
        settings.Remember(first, TimeSpan.FromMinutes(12), TimeSpan.FromMinutes(24));

        var (session, app, backend) = Host(settings);
        using var _s = session;
        app.Open(first);
        session.PumpFrames(4);

        backend.Advance(TimeSpan.FromMinutes(30));         // past the end
        session.PumpFrames(4);

        Assert.EndsWith("Show - 02.mkv", app.CurrentFile);
        Assert.Null(settings.ResumeFor(first));
        Assert.True(app.Player!.IsPlaying);
        Assert.Contains(backend.Calls, c => c.StartsWith("Open(", StringComparison.Ordinal) && c.Contains("Show - 02.mkv", StringComparison.Ordinal));
    }

    /// <summary>
    /// The volume and mute the overlay's keys set are what the next file opens with, and the
    /// window remembers them on close. Setting them costs the app no rebuild: the player
    /// rebuilds itself, and the app only notes the value.
    /// </summary>
    [Fact]
    public void TheVolumeYouLeaveIsTheOneTheNextFileOpensWith()
    {
        var first = File_("Show - 01.mkv");
        File_("Show - 02.mkv");

        var (session, app, _) = Host();
        using var _s = session;
        app.Open(first);
        session.PumpFrames(4);
        int builds = app.Builds;

        app.Player!.Volume = 0.4f;
        app.Player.Muted = true;
        session.PumpFrames(2);

        Assert.Equal(0.4f, app.Settings.Data.Volume);
        Assert.True(app.Settings.Data.Muted);
        Assert.Equal(builds, app.Builds);

        Assert.True(app.Step(1));
        session.PumpFrames(2);
        Assert.Equal(0.4f, app.Player!.Volume);
        Assert.True(app.Player.Muted);
    }

    // ── The chapters the file carries ────────────────────────────────────

    [Fact]
    public void TheContainersChapterMarksReachTheScrubber()
    {
        var path = File_("film.mkv");
        var session = HeadlessSession.Create(900, 600);
        using var _s = session;
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(24) };
        backend.Chapters.Add(new MediaChapterMark(TimeSpan.Zero, "Opening"));
        backend.Chapters.Add(new MediaChapterMark(TimeSpan.FromMinutes(2), "Titles"));
        backend.Chapters.Add(new MediaChapterMark(TimeSpan.FromMinutes(22), "Ending"));

        var app = new ReelApp(session.Application, session.Window.Window,
            new ReelSettings(Path.Combine(_folder, "settings.json")), () => backend);
        session.Application.SetRoot(app.Mount());
        app.Open(path);
        session.PumpFrames(4);

        Assert.Equal(3, app.Player!.Chapters.Count);
        Assert.Equal(3, app.VideoPlayer!.ChapterMarks.Count);
        Assert.Equal(TimeSpan.FromMinutes(2), app.VideoPlayer.ChapterMarks[1]);
    }

    // ── With nothing open ────────────────────────────────────────────────

    [Fact]
    public void AnEmptyWindowInvitesAFileRatherThanLookingBroken()
    {
        var (session, _, _) = Host();
        using var _s = session;

        var dump = session.Window.DumpAccessibilityTree();
        Assert.Contains("Nothing open", dump, StringComparison.Ordinal);
        Assert.Contains("Ctrl+O", dump, StringComparison.Ordinal);
        Assert.NotNull(session.Window.FindByAutomationId("reel-open"));
    }

    [Fact]
    public void AMissingFileIsSaidPlainlyAndLeavesThePlayerAlone()
    {
        var (session, app, _) = Host();
        using var _s = session;

        app.Open(Path.Combine(_folder, "not-here.mkv"));
        session.PumpFrames(2);

        Assert.Null(app.CurrentFile);
        Assert.Contains("not-here.mkv", session.Window.DumpAccessibilityTree(), StringComparison.Ordinal);
    }

    [Fact]
    public void AMachineWithNoDecoderSaysSoInsteadOfRefusingToStart()
    {
        var session = HeadlessSession.Create(900, 600);
        using var _s = session;
        var app = new ReelApp(session.Application, session.Window.Window,
            new ReelSettings(Path.Combine(_folder, "settings.json")), backends: null);
        session.Application.SetRoot(app.Mount());
        session.PumpFrames(2);

        Assert.Contains("No libmpv found", session.Window.DumpAccessibilityTree(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The open dialog's filters, against the format <see cref="FileDialogFilter"/> documents:
    /// bare extensions separated by semicolons. Reel shipped with the Windows common-dialog
    /// shape instead ("*.mkv;*.mp4"), and SDL answers a pattern it cannot parse with a null
    /// file list — which the platform layer resolves to "no path chosen", the same as a
    /// cancel. Ctrl+O did nothing, said nothing, and logged nothing.
    /// </summary>
    [Fact]
    public void TheOpenDialogsFiltersAreBareExtensions()
    {
        var filters = (FileDialogFilter[])typeof(ReelApp)
            .GetField("Filters", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

        var videos = Assert.Single(filters, f => f.Label == "Video files");
        var patterns = videos.Pattern.Split(';');

        Assert.DoesNotContain(patterns, p => p.Contains('.') || p.Contains('*'));
        // Every format the playlist walks is a format the dialog offers, or the two
        // disagree about what Reel plays.
        Assert.Equal(
            Playlist.Extensions.Select(e => e.TrimStart('.')).Order(),
            patterns.Order());

        // "*" is the one pattern that is allowed to be a glob, and it means everything.
        Assert.Equal("*", Assert.Single(filters, f => f.Label == "All files").Pattern);
    }

    // ── Track memory ─────────────────────────────────────────────────────

    /// <summary>
    /// The point of remembering by language: episode two of a dual-audio release opens on
    /// the dub the viewer chose during episode one, without being asked again.
    /// </summary>
    [Fact]
    public void TheAudioTrackYouPickIsTheOneTheNextFileOpensWith()
    {
        var first = File_("Show - 01.mkv");
        File_("Show - 02.mkv");

        var (session, app, backend) = Host();
        using var _s = session;
        backend.AudioTracks.Add(new MediaTrack(1, "Japanese", "jpn"));
        backend.AudioTracks.Add(new MediaTrack(2, "English", "eng"));

        app.Open(first);
        session.PumpFrames(4);

        // The file opens on its own default; the viewer switches, as the overlay's audio
        // menu does — straight to the player.
        Assert.Equal(1, app.Player!.SelectedAudioTrack);
        app.Player!.SelectAudioTrack(2);
        session.PumpFrames(4);

        Assert.True(app.Step(1));
        session.PumpFrames(4);

        Assert.EndsWith("Show - 02.mkv", app.CurrentFile);
        Assert.Equal(2, app.Player!.SelectedAudioTrack);
        Assert.Equal("eng", app.Settings.Data.PreferredAudioLanguage);
    }

    /// <summary>Turning subtitles off is a preference too, and the one most easily undone by
    /// a player that re-reads the file's default every time.</summary>
    [Fact]
    public void SubtitlesSwitchedOffStayOff()
    {
        var first = File_("Show - 01.mkv");
        File_("Show - 02.mkv");

        var (session, app, backend) = Host();
        using var _s = session;
        backend.AudioTracks.Add(new MediaTrack(1, "English", "eng"));
        backend.SubtitleTracks.Add(new MediaTrack(5, "English", "eng"));

        app.Open(first);
        session.PumpFrames(4);

        app.Player!.SelectSubtitleTrack(5);        // on…
        session.PumpFrames(4);
        app.Player!.SelectSubtitleTrack(null);     // …and off again
        session.PumpFrames(4);

        Assert.False(app.Settings.Data.SubtitlesOn);
        Assert.Equal("eng", app.Settings.Data.PreferredSubtitleLanguage);

        Assert.True(app.Step(1));
        session.PumpFrames(4);
        Assert.Null(app.Player!.SelectedSubtitleTrack);
    }

    /// <summary>
    /// The remembering is one-way. A file with no track in the preferred language must not
    /// quietly rewrite the preference to whatever it happened to open with, or one
    /// Japanese-only extra would undo an evening of English.
    /// </summary>
    [Fact]
    public void AFileWithoutThePreferredLanguageDoesNotOverwriteIt()
    {
        var only = File_("Extra.mkv");

        var settings = new ReelSettings(Path.Combine(_folder, "settings.json"));
        settings.Data.PreferredAudioLanguage = "eng";

        var (session, app, backend) = Host(settings);
        using var _s = session;
        backend.AudioTracks.Add(new MediaTrack(1, "Japanese", "jpn"));

        app.Open(only);
        session.PumpFrames(6);

        Assert.Equal(1, app.Player!.SelectedAudioTrack);          // nothing else to play
        Assert.Equal("eng", app.Settings.Data.PreferredAudioLanguage);
    }

    // ── Chapters ─────────────────────────────────────────────────────────

    private static void Chapters(FakeMediaBackend backend, params int[] startMinutes)
    {
        for (int i = 0; i < startMinutes.Length; i++)
            backend.Chapters.Add(new MediaChapterMark(TimeSpan.FromMinutes(startMinutes[i]),
                i == 0 ? "Opening" : null));
    }

    /// <summary>
    /// The Page keys walk chapters inside a chaptered file. Before this they walked files,
    /// which on a ripped disc meant there was no way to reach a mark at all.
    /// </summary>
    [Fact]
    public void ThePageKeysStepChaptersInsideAChapteredFile()
    {
        var first = File_("Film.mkv");
        var (session, app, backend) = Host();
        using var _s = session;
        Chapters(backend, 0, 10, 20);

        app.Open(first);
        session.PumpFrames(4);

        session.Window.PressKey(KeyCode.PageDown);
        session.PumpFrames(2);
        Assert.Equal(TimeSpan.FromMinutes(10), app.Player!.Position);

        session.Window.PressKey(KeyCode.PageDown);
        session.PumpFrames(2);
        Assert.Equal(TimeSpan.FromMinutes(20), app.Player!.Position);
    }

    /// <summary>Back from inside a chapter restarts it; back again leaves it. The rule every
    /// CD player has followed since 1982, and the reason two presses reach the one before.</summary>
    [Fact]
    public void BackRestartsTheChapterBeforeItLeavesIt()
    {
        var first = File_("Film.mkv");
        var (session, app, backend) = Host();
        using var _s = session;
        Chapters(backend, 0, 10, 20);

        app.Open(first);
        session.PumpFrames(4);
        app.Player!.Seek(TimeSpan.FromMinutes(12));       // a minute or two into chapter two
        session.PumpFrames(2);

        session.Window.PressKey(KeyCode.PageUp);
        session.PumpFrames(2);
        Assert.Equal(TimeSpan.FromMinutes(10), app.Player!.Position);

        session.Window.PressKey(KeyCode.PageUp);
        session.PumpFrames(2);
        Assert.Equal(TimeSpan.Zero, app.Player!.Position);
    }

    /// <summary>A file with no marks is the common case, and the Page keys have to go on
    /// meaning what they meant before: the next file.</summary>
    [Fact]
    public void WithoutChaptersThePageKeysStillWalkTheFolder()
    {
        var first = File_("Show - 01.mkv");
        File_("Show - 02.mkv");

        var (session, app, _) = Host();
        using var _s = session;
        app.Open(first);
        session.PumpFrames(4);

        session.Window.PressKey(KeyCode.PageDown);
        session.PumpFrames(4);
        Assert.EndsWith("Show - 02.mkv", app.CurrentFile);
    }

    /// <summary>Past the last chapter, the Page key rolls on to the next file rather than
    /// doing nothing — one key for the whole evening.</summary>
    [Fact]
    public void PastTheLastChapterThePageKeyGoesToTheNextFile()
    {
        var first = File_("Show - 01.mkv");
        File_("Show - 02.mkv");

        var (session, app, backend) = Host();
        using var _s = session;
        Chapters(backend, 0, 10);

        app.Open(first);
        session.PumpFrames(4);
        app.Player!.Seek(TimeSpan.FromMinutes(15));       // inside the last chapter
        session.PumpFrames(2);

        session.Window.PressKey(KeyCode.PageDown);
        session.PumpFrames(4);
        Assert.EndsWith("Show - 02.mkv", app.CurrentFile);
    }

    /// <summary>C opens the list, and a mark the container did not name is still called
    /// something. Escape closes it before it does anything else.</summary>
    [Fact]
    public void TheChapterPanelNamesEveryMarkAndClosesOnEscape()
    {
        var first = File_("Film.mkv");
        var (session, app, backend) = Host();
        using var _s = session;
        Chapters(backend, 0, 10, 20);

        app.Open(first);
        session.PumpFrames(4);

        session.Window.PressKey(KeyCode.C);
        session.PumpFrames(2);
        Assert.True(app.ChaptersOpen);

        var tree = session.Window.DumpAccessibilityTree();
        Assert.Contains("Opening", tree, StringComparison.Ordinal);
        Assert.Contains("Chapter 2", tree, StringComparison.Ordinal);   // unnamed in the file
        Assert.Contains("Chapter 3", tree, StringComparison.Ordinal);

        session.Window.PressKey(KeyCode.Escape);
        session.PumpFrames(2);
        Assert.False(app.ChaptersOpen);
    }

    /// <summary>
    /// The panel marks the chapter playing now and follows the playhead, rebuilding the app
    /// only when the playhead crosses into another chapter. It read the position in the app's
    /// build, so while it was open every position report (ten a second) rebuilt the whole app.
    /// </summary>
    [Fact]
    public void TheChapterPanelFollowsThePlayhead_RebuildingOnlyWhenTheChapterChanges()
    {
        var first = File_("Film.mkv");
        var (session, app, backend) = Host();
        using var _s = session;
        Chapters(backend, 0, 10, 20);

        app.Open(first);
        session.PumpFrames(4);
        session.Window.PressKey(KeyCode.C);
        session.PumpFrames(2);
        Assert.NotNull(session.Window.FindByRole(HultUI.Core.Accessibility.AccessibilityRole.Button, "Opening, playing now"));
        int builds = app.Builds;

        // Eighteen reports inside the first chapter.
        for (int s = 30; s < 600; s += 32)
        {
            backend.Raise(new BackendEvent.PositionChanged(TimeSpan.FromSeconds(s)));
            session.PumpFrame();
        }
        Assert.Equal(builds, app.Builds);

        backend.Raise(new BackendEvent.PositionChanged(TimeSpan.FromMinutes(10.5)));
        session.PumpFrame();
        Assert.Equal(builds + 1, app.Builds);
        Assert.NotNull(session.Window.FindByRole(HultUI.Core.Accessibility.AccessibilityRole.Button, "Chapter 2, playing now"));
        Assert.Null(session.Window.FindByRole(HultUI.Core.Accessibility.AccessibilityRole.Button, "Opening, playing now"));
    }

    /// <summary>Clicking a row seeks there and puts the panel away.</summary>
    [Fact]
    public void ClickingAChapterSeeksToItAndClosesThePanel()
    {
        var first = File_("Film.mkv");
        var (session, app, backend) = Host();
        using var _s = session;
        Chapters(backend, 0, 10, 20);

        app.Open(first);
        session.PumpFrames(4);
        app.ToggleChapters();
        session.PumpFrames(2);

        session.Window.FindByAutomationId("chapter-2")!.Click();
        session.PumpFrames(2);

        Assert.Equal(TimeSpan.FromMinutes(20), app.Player!.Position);
        Assert.False(app.ChaptersOpen);
    }

    /// <summary>A file with no chapters has no panel to open, so C is not a key that does
    /// nothing visible — it is a key the app leaves alone.</summary>
    [Fact]
    public void WithoutChaptersThereIsNoPanel()
    {
        var first = File_("Film.mkv");
        var (session, app, _) = Host();
        using var _s = session;

        app.Open(first);
        session.PumpFrames(4);
        app.ToggleChapters();
        session.PumpFrames(2);

        Assert.False(app.ChaptersOpen);
        Assert.Null(session.Window.FindByAutomationId("chapters"));
    }

    // ── Scrubber stills ──────────────────────────────────────────────────

    /// <summary>Stills made one at a time as the test lets them through, as Reel's background
    /// decoder makes them: each the same small PNG.</summary>
    private sealed class HeldStills : IScrubFrameSource
    {
        private readonly SemaphoreSlim _let = new(0);
        private static readonly byte[] Png = MakePng();
        private int _made;

        public int Made => Volatile.Read(ref _made);
        public void Let(int count) => _let.Release(count);
        public Task<bool> OpenAsync(string path, CancellationToken cancellationToken) => Task.FromResult(true);

        public async Task<byte[]?> GrabAsync(TimeSpan position, CancellationToken cancellationToken)
        {
            await _let.WaitAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _made);
            return Png;
        }

        public void Dispose() { }

        private static byte[] MakePng()
        {
            using var bitmap = new SkiaSharp.SKBitmap(64, 36);
            bitmap.Erase(new SkiaSharp.SKColor(200, 80, 40));
            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
            return data.ToArray();
        }
    }

    /// <summary>The time the scrubber's preview card reads, or null with no card.</summary>
    private static string? CardTime(HeadlessSession session) =>
        session.Window.FindByAutomationId("video-preview-card")?.Element.Children
            .OfType<HultUI.Core.Elements.TextElement>().FirstOrDefault()?.Content;

    /// <summary>Rests the pointer halfway along the scrubber: 12:00 of the 24-minute film.</summary>
    private static void RestOnTheScrubber(HeadlessSession session)
    {
        var track = session.Window.FindByAutomationId("video-seek")!.Element.Layout.AbsoluteContentBox;
        session.Window.MoveMouse(track.X + track.Width / 2f, track.Y + track.Height / 2f);
        session.PumpFrames(2);
    }

    [Fact]
    public void TheNextFileInTheFolderHasScrubberStillsToo()
    {
        // Each file gets a player of its own; the stills used to be handed to the first one only,
        // so from the second file in a folder on, the scrubber had no preview.
        var first = File_("a.mkv");
        File_("b.mkv");
        var (session, app, _) = Host();
        using var _s = session;
        var stills = new HeldStills();
        app.ScrubFrameSource = () => stills;
        app.Open(first);
        session.PumpFrames(4);
        Assert.True(app.Step(1));
        session.PumpFrames(4);
        app.Player!.Pause();                  // the bar stays up while paused
        session.PumpFrames(2);

        stills.Let(120);
        session.PumpUntil(() => app.VideoPlayer!.PreviewFrame?.Invoke(TimeSpan.FromMinutes(12))?.Time
            == TimeSpan.FromMinutes(12), TimeSpan.FromSeconds(10));
        RestOnTheScrubber(session);

        Assert.EndsWith("b.mkv", app.CurrentFile);
        Assert.Equal("12:00", CardTime(session));
    }

    [Fact]
    public void AStillLandingUnderAPointerRestingOnTheScrubber_ShowsAtOnce_AndRebuildsNothingElse()
    {
        // A still used to rebuild the whole app as it landed, which never rebuilt the player that
        // shows the card: 120 app builds a file for nothing, and a pointer resting on the
        // scrubber saw a new still only when it next moved. The player now asks again itself.
        var path = File_("film.mkv");
        var (session, app, _) = Host();
        using var _s = session;
        var stills = new HeldStills();
        app.ScrubFrameSource = () => stills;
        app.Open(path);
        session.PumpFrames(4);
        app.Player!.Pause();                  // the bar stays up while paused
        session.PumpFrames(2);
        RestOnTheScrubber(session);
        Assert.Null(CardTime(session));       // no still yet: no card
        int builds = app.Builds;

        // The first three (0:00, 0:12, 0:24): the card comes with the nearest to 12:00.
        stills.Let(3);
        session.PumpUntil(() => CardTime(session) == "0:24", TimeSpan.FromSeconds(10));

        // The one for 12:00 itself: the card on screen takes it, the pointer never having moved.
        stills.Let(58);
        session.PumpUntil(() => CardTime(session) == "12:00", TimeSpan.FromSeconds(10));

        // And the rest of the file: 120 stills, not one build of the app.
        stills.Let(59);
        session.PumpUntil(() => app.VideoPlayer!.PreviewFrame?.Invoke(TimeSpan.FromMinutes(24))?.Time
            == TimeSpan.FromSeconds(119 * 12), TimeSpan.FromSeconds(10));
        session.PumpFrames(3);
        Assert.Equal(120, stills.Made);
        Assert.Equal(builds, app.Builds);
        Assert.Equal("12:00", CardTime(session));
    }
}
