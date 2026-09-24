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
}
