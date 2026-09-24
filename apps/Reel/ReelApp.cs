using HultUI;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Icons;
using HultUI.Media;

namespace Reel;

using Text = HultUI.Core.Builders.Text;

/// <summary>
/// Reel: a local video player, and nothing else. The picture and its transport bar are
/// <see cref="HultUI.Media.VideoPlayer"/> — the same overlay the Jellyfin client uses — so
/// what lives here is only what a file needs and a library does not: the file itself, the
/// folder it sits in as a running order, where it was left, and the chapters the container
/// carries.
///
/// <para>Deliberately not a browser. It opens what it is given, plays it, remembers where
/// you stopped, and stays out of the way — which is the whole difference from Aperture,
/// where playback is a sheet over a library.</para>
/// </summary>
public sealed class ReelApp : Component, IDisposable
{
    private static readonly FileDialogFilter[] Filters =
    [
        new("Video files", string.Join(';', Playlist.Extensions.Select(e => "*" + e))),
        new("All files", "*"),
    ];

    /// <summary>How often the resume point is written while playing. Often enough that a
    /// power cut costs seconds, rarely enough that it is not a write per frame.</summary>
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(15);

    private readonly Application _app;
    private readonly Window _window;
    private readonly ReelSettings _settings;
    private readonly Func<IMediaBackend>? _backends;

    private MediaPlayer? _player;
    private HultUI.Media.VideoPlayer? _videoPlayer;
    private Playlist? _playlist;

    /// <summary>Bumped to force a rebuild; every async hop lands through here.</summary>
    private readonly HultUI.Core.Reactivity.Signal<int> _revision = new(0);

    /// <summary>Where the open file should start, and whether that seek has happened yet.
    /// mpv cannot be told to open at a position, so the seek waits for the load.</summary>
    private TimeSpan _resumeTo;
    private bool _resumeApplied = true;

    private long _lastSaveTicks;
    private int _chapterCount = -1;
    private string? _error;
    private bool _opening;

    public ReelApp(Application app, Window window, ReelSettings? settings = null,
        Func<IMediaBackend>? backends = null)
    {
        _app = app;
        _window = window;
        _settings = settings ?? new ReelSettings();
        _backends = backends;

        Mount().On<KeyDownEvent>(OnKey);
    }

    /// <summary>The file on screen, or null when nothing is open.</summary>
    public string? CurrentFile { get; private set; }

    internal MediaPlayer? Player => _player;
    internal HultUI.Media.VideoPlayer? VideoPlayer => _videoPlayer;
    internal Playlist? Playlist => _playlist;
    internal ReelSettings Settings => _settings;

    private void Rerender() => _revision.Value++;

    // ── Opening ──────────────────────────────────────────────────────────

    /// <summary>
    /// Plays a file. Everything else follows from it: the folder becomes the running
    /// order, the window takes its name, and the resume point is looked up and applied
    /// once the backend reports the file loaded.
    /// </summary>
    public void Open(string path, bool resume = true)
    {
        if (_backends is null)
        {
            _error = "No libmpv on this machine — see src/HultUI.Media.Mpv/README.md";
            Rerender();
            return;
        }
        if (string.IsNullOrWhiteSpace(path)) return;

        var full = TryFullPath(path);
        if (full is null || !File.Exists(full))
        {
            _error = $"Cannot open {Path.GetFileName(path)} — it is not there";
            Rerender();
            return;
        }

        SaveResumePoint();
        _error = null;
        CurrentFile = full;
        _playlist = Reel.Playlist.ForFile(full);
        _settings.Data.LastFolder = Path.GetDirectoryName(full);

        _resumeTo = resume ? _settings.ResumeFor(full) ?? TimeSpan.Zero : TimeSpan.Zero;
        _resumeApplied = _resumeTo <= TimeSpan.Zero;
        _chapterCount = -1;

        OpenStream(full);
        _window.Title = WindowTitle();
        Rerender();
    }

    /// <summary>A fresh player per file: mpv is told to open the next one, and a backend
    /// that has ended is not a backend that plays again.</summary>
    private void OpenStream(string path)
    {
        _player?.Dispose();
        _player = new MediaPlayer(_backends!());
        _player.Volume = _settings.Data.Volume;
        _player.Muted = _settings.Data.Muted;

        _videoPlayer = new HultUI.Media.VideoPlayer(_player) { JumpStep = TimeSpan.FromSeconds(10) };
        _videoPlayer.FullscreenRequested += ToggleFullscreen;
        // The headless driver pumps frames far faster than wall time, so the bar would
        // fade before any shot of it playing.
        if (Environment.GetEnvironmentVariable("HULTUI_MEDIA_NOHIDE") is "1")
            _videoPlayer.AutoHideControls = false;

        _player.Open(MediaSource.FromFile(path), autoPlay: true);
        _videoPlayer.FocusPicture();
        _lastSaveTicks = Environment.TickCount64;
    }

    /// <summary>Asks for a file. The dialog starts where the last one came from, which for
    /// a player is almost always the right folder.</summary>
    public void OpenDialog()
    {
        if (!_window.FileDialogs.IsAvailable) return;
        _ = OpenDialogAsync();
    }

    private async Task OpenDialogAsync()
    {
        if (_opening) return;
        _opening = true;
        try
        {
            var picked = await _window.FileDialogs.OpenFileAsync(_settings.Data.LastFolder, Filters)
                .ConfigureAwait(false);
            if (picked is null) return;
            Application.InvokeOnMainThread(() => Open(picked));
        }
        finally { _opening = false; }
    }

    /// <summary>The next or previous file in the folder. Nothing happens at either end:
    /// the last episode of a season is the end of the evening, not the first again.</summary>
    public bool Step(int direction)
    {
        if (_playlist is not { } playlist) return false;
        var next = direction > 0 ? playlist.Next() : playlist.Previous();
        if (next is null) return false;
        Open(next);
        return true;
    }

    private void ToggleFullscreen()
    {
        _window.ToggleFullscreen();
        if (_videoPlayer is { } video) video.IsFullscreen = _window.IsFullscreen;
        Rerender();
    }

    private string WindowTitle()
    {
        if (CurrentFile is null) return "Reel";
        var name = Path.GetFileNameWithoutExtension(CurrentFile);
        var place = _playlist?.Position;
        return string.IsNullOrEmpty(place) ? $"{name} — Reel" : $"{name} — {place} — Reel";
    }

    private static string? TryFullPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        { return null; }
    }

    // ── The frame's housekeeping ─────────────────────────────────────────

    /// <summary>
    /// Runs on every build while something is open: applies the resume seek once the file
    /// has loaded, hands the container's chapter marks to the transport bar, writes the
    /// resume point periodically, and moves to the next file when this one ends.
    /// </summary>
    private void Pump()
    {
        if (_player is not { } player || _videoPlayer is not { } video) return;

        if (!_resumeApplied && player.HasMedia && player.Duration is { } length && length > TimeSpan.Zero)
        {
            player.Seek(_resumeTo);
            _resumeApplied = true;
        }

        // The marks arrive with the file, a moment after it opens.
        var chapters = player.Chapters;
        if (chapters.Count != _chapterCount)
        {
            _chapterCount = chapters.Count;
            video.ChapterMarks = [.. chapters.Select(c => c.Start)];
        }

        _settings.Data.Volume = player.Volume;
        _settings.Data.Muted = player.Muted;

        long now = Environment.TickCount64;
        if (player.IsPlaying && now - _lastSaveTicks >= (long)SaveInterval.TotalMilliseconds)
        {
            _lastSaveTicks = now;
            SaveResumePoint();
        }

        if (player.State == PlaybackState.Ended)
        {
            SaveResumePoint();
            // Ended means finished: the point is cleared by Remember, and the folder rolls on.
            if (!Step(1)) Rerender();
        }
    }

    /// <summary>Writes where the open file has reached. Called on a timer while playing, on
    /// every file change, and as the window closes — the last one is what makes a player
    /// trustworthy.</summary>
    internal void SaveResumePoint()
    {
        if (CurrentFile is not { } path || _player is not { } player) return;
        if (!player.HasMedia) return;
        _settings.Remember(path, player.Position, player.Duration);
    }

    // ── Keys ─────────────────────────────────────────────────────────────

    private void OnKey(KeyDownEvent e)
    {
        // The transport keys belong to the overlay; these are the app's own.
        switch (e.Key)
        {
            case KeyCode.O when e.Modifiers.HasFlag(KeyModifiers.Ctrl):
                OpenDialog();
                e.Handled = true;
                break;
            case KeyCode.PageDown or KeyCode.N:
                if (Step(1)) e.Handled = true;
                break;
            case KeyCode.PageUp or KeyCode.P:
                if (Step(-1)) e.Handled = true;
                break;
            case KeyCode.Escape when _window.IsFullscreen:
                ToggleFullscreen();
                e.Handled = true;
                break;
        }
    }

    // ── The window ───────────────────────────────────────────────────────

    public override Element Build()
    {
        _ = _revision.Value;
        Pump();

        var root = Flex.Column(gap: 0).FlexGrow(1)
            .Background(Color.Black)
            .AutomationId("reel")
            .OnDragOver(e => e.Accepted = e.DragData.HasFiles)
            .OnDrop(e =>
            {
                if (!e.DragData.HasFiles) return;
                var file = e.DragData.Files.FirstOrDefault(Reel.Playlist.IsPlayable) ?? e.DragData.Files[0];
                Open(file);
            });

        if (_videoPlayer is { } video && _error is null)
            root.AddChild(video.Mount().FlexGrow(1));
        else
            root.AddChild(EmptyState());

        return root;
    }

    /// <summary>
    /// What the window says with nothing open. Three different sentences on purpose: no
    /// decoder is a broken install, a failed open is about that file, and an empty window
    /// is an invitation.
    /// </summary>
    private Element EmptyState()
    {
        var column = Flex.Column(gap: 12)
            .FlexGrow(1)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .AutomationId("reel-empty");

        bool broken = _backends is null;
        column.AddChild(Phosphor.Draw(broken ? "warning" : "film-slate", 40f, new Color(120, 120, 130)));

        column.AddChild(Text.New(broken ? "No libmpv found" : _error ?? "Nothing open")
            .FontSize(18f).FontWeight(FontWeight.SemiBold).TextColor(new Color(226, 232, 240)));

        column.AddChild(Text.New(broken
                ? "Reel plays through libmpv. Put it beside the app, or point HULTUI_MPV_PATH at it."
                : "Open a file with Ctrl+O, or drop one on the window.")
            .FontSize(13f).TextColor(new Color(148, 163, 184)));

        if (!broken)
        {
            // Sized rather than left to stretch: the column centres its children, but a
            // button with nothing to size it fills the width of the window.
            var open = Button.New("Open…")
                .Width(160f)
                .OnClick(_ => OpenDialog())
                .AutomationId("reel-open");
            open.AccessibleName = "Open a video";
            column.AddChild(open);
            if (DefaultPlayerOffer() is { } offer) column.AddChild(offer);
        }
        return column;
    }

    /// <summary>
    /// The line under the Open button on Windows: register the associations, then send the
    /// user to the page where the choice is actually made. Windows has not let an
    /// application appoint itself the default since 8, so this asks rather than does — and
    /// says so, because a button that looked like it had failed would be worse.
    /// </summary>
    private Element? DefaultPlayerOffer()
    {
        if (!OperatingSystem.IsWindows()) return null;

        var column = Flex.Column(gap: 6).AlignItems(AlignItems.Center);
        column.AddChild(Box.New().Height(10f));

        if (_associationsAsked)
        {
            column.AddChild(Text.New("Reel is registered — pick it under Video player in Settings.")
                .FontSize(12f).TextColor(new Color(148, 163, 184)));
            return column;
        }

        var button = Button.New("Make Reel the default…", ButtonVariant.Secondary)
            .Width(220f)
            .OnClick(_ => RegisterAsPlayer())
            .AutomationId("reel-register");
        button.AccessibleName = "Register Reel as a video player";
        column.AddChild(button);
        column.AddChild(Text.New(FileAssociations.IsRegistered()
                ? "Registered. Windows asks you to confirm the change itself."
                : "Windows asks you to confirm the change itself.")
            .FontSize(11.5f).TextColor(new Color(120, 130, 145)));
        return column;
    }

    private bool _associationsAsked;

    /// <summary>Registers the file types and opens the Settings page. Both halves are
    /// needed: the registration is what puts Reel on that page at all.</summary>
    internal void RegisterAsPlayer()
    {
        if (!OperatingSystem.IsWindows()) return;
        FileAssociations.Register();
        FileAssociations.OpenDefaultAppsSettings();
        _associationsAsked = true;
        Rerender();
    }

    public void Dispose()
    {
        SaveResumePoint();
        _settings.Save();
        _player?.Dispose();
        _player = null;
    }
}
