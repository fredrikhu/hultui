using Aperture.Core;
using Aperture.Theme;
using HultUI;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Media;

namespace Aperture;

/// <summary>
/// What happens when an episode ends: the next one, on a countdown you can stop — and,
/// after enough of them in a row, a question instead.
///
/// <para>The question is the point. Auto-play is how a series gets watched and also how a
/// server ends up streaming to a dark room until morning; every client that does one has
/// to do the other. Three episodes in a row is the threshold here, chosen because it is
/// about ninety minutes of television — long enough not to nag someone who is watching,
/// short enough to catch someone who is asleep.</para>
/// </summary>
public sealed partial class ApertureApp
{
    /// <summary>How long the next episode waits before it starts itself.</summary>
    internal static readonly TimeSpan UpNextCountdown = TimeSpan.FromSeconds(12);

    /// <summary>Episodes started by the countdown rather than by a person, in a row. Reset
    /// by any deliberate play, and by the question being answered.</summary>
    private int _autoPlayedInARow;

    /// <summary>After this many, the countdown stops and asks.</summary>
    internal const int AutoPlaysBeforeAsking = 3;

    /// <summary>The episode the prompt is offering, or null when no prompt is up.</summary>
    private MediaItem? _upNext;

    /// <summary>The item whose ending has already been dealt with. The player stays
    /// <see cref="PlaybackState.Ended"/> for as long as the file sits at its last frame, so
    /// without this a dismissed prompt would come back with the next change of state the
    /// playback watch sees.</summary>
    private string? _endHandledFor;

    /// <summary>When the countdown expires. Null while the prompt is waiting for an answer
    /// rather than counting — which is what pass-out protection is.</summary>
    private long? _upNextDeadline;

    /// <summary>Repaints the countdown. A timer rather than a tween because the sheet is
    /// otherwise idle once playback has ended: nothing else would mark it dirty, and a
    /// number that only moves when the pointer does is worse than no number.</summary>
    private System.Threading.Timer? _upNextTimer;

    internal MediaItem? UpNextItem => _upNext;
    internal bool UpNextIsAsking => _upNext is not null && _upNextDeadline is null;

    /// <summary>Seconds left on the countdown, for the view and the tests.</summary>
    internal int UpNextSecondsLeft => _upNextDeadline is { } deadline
        ? Math.Max(0, (int)Math.Ceiling((deadline - Environment.TickCount64) / 1000.0))
        : 0;

    /// <summary>
    /// Called from the playback watch (<see cref="OnPlaybackChanged"/>) when the backend
    /// reports the file ended. Asks the server what comes next and puts the prompt up; a film,
    /// or the last episode of a series, simply stops.
    /// </summary>
    private void OnPlaybackEnded()
    {
        if (_library is not { } lib || _playingItem is not { } finished) return;
        if (_upNext is not null) return;               // already offering something
        if (_endHandledFor == finished.Id) return;     // this ending has been answered
        _endHandledFor = finished.Id;

        // A queue already knows what follows — it was decided when Play all was pressed,
        // and asking the server instead would quietly leave the list.
        if (QueueNext is { } queued)
        {
            OfferNext(queued);
            return;
        }

        // Asked by place in the series (the episode list from this one on), not the server's
        // next-up, so the answer does not wait on the server marking this one watched — which
        // it does at the stop report, when the next one starts or the picture closes.
        _ = FindNextAsync(lib, finished);
    }

    private async Task FindNextAsync(IMediaLibrary lib, MediaItem finished)
    {
        var next = await lib.GetNextEpisodeAsync(finished).ConfigureAwait(false);
        Application.InvokeOnMainThread(() =>
        {
            // Playback moved on while we asked — a different episode, or stopped.
            if (_playingItem?.Id != finished.Id) return;
            if (!next.Ok || next.Value is null)
            {
                // Nothing follows: leave the picture where it ended rather than dropping
                // the viewer back into the library mid-credits.
                return;
            }
            OfferNext(next.Value);
        });
    }

    /// <summary>Puts the prompt up — counting down, or asking, depending on how many
    /// episodes have started themselves already.</summary>
    private void OfferNext(MediaItem next)
    {
        _upNext = next;
        _upNextDeadline = _autoPlayedInARow >= AutoPlaysBeforeAsking
            ? null
            : Environment.TickCount64 + (long)UpNextCountdown.TotalMilliseconds;

        if (_upNextDeadline is not null) StartUpNextTimer();
        Rerender();
    }

    private void StartUpNextTimer()
    {
        _upNextTimer?.Dispose();
        // Quarter-second ticks: the number on screen only changes once a second, but a
        // tick that lands just after a second boundary would show it a beat late.
        _upNextTimer = new System.Threading.Timer(
            _ => Application.InvokeOnMainThread(UpNextTick), null, 250, 250);
    }

    private void UpNextTick()
    {
        if (_upNext is null || _upNextDeadline is null) { StopUpNextTimer(); return; }
        if (UpNextSecondsLeft <= 0) PlayUpNext();
        else Rerender();
    }

    private void StopUpNextTimer()
    {
        _upNextTimer?.Dispose();
        _upNextTimer = null;
    }

    /// <summary>Starts the offered episode. Counts as an auto-play only when the countdown
    /// ran out on its own — pressing the button is a person saying yes, which is exactly
    /// what pass-out protection is looking for.</summary>
    internal void PlayUpNext(bool deliberate = false)
    {
        if (_upNext is not { } next) return;
        _autoPlayedInARow = deliberate ? 0 : _autoPlayedInARow + 1;
        DismissUpNext();

        // Inside a queue, "next" means the next row — advancing keeps the place, where
        // playing the item directly would drop out of the list.
        if (QueueNext is { } queued && queued.Id == next.Id)
        {
            AdvanceQueue();
            return;
        }
        Play(next, next.ResumePosition ?? TimeSpan.Zero, countsAsDeliberate: false);
    }

    /// <summary>A new file is opening, so the last one's ending is history.</summary>
    private void ForgetPlaybackEnding() => _endHandledFor = null;

    /// <summary>Takes the prompt down without playing anything. The picture stays where it
    /// ended; Escape still stops.</summary>
    internal void DismissUpNext()
    {
        _upNext = null;
        _upNextDeadline = null;
        StopUpNextTimer();
        Rerender();
    }

    /// <summary>Answers the "still watching?" question: the countdown starts over and the
    /// run resets, so the next three go by without asking again.</summary>
    internal void ConfirmStillWatching()
    {
        if (_upNext is null) return;
        _autoPlayedInARow = 0;
        _upNextDeadline = Environment.TickCount64 + (long)UpNextCountdown.TotalMilliseconds;
        StartUpNextTimer();
        Rerender();
    }

    // ── The card ─────────────────────────────────────────────────────────

    /// <summary>
    /// The prompt, bottom-right over the ended picture: what is next, and how long until it
    /// starts. Bottom-right rather than centred so the last frame of what you just watched
    /// is still visible behind it.
    /// </summary>
    private Element BuildUpNextCard(MediaItem next)
    {
        var card = Flex.Row(gap: Halide.S4)
            .Padding(new Thickness(Halide.S4))
            .CornerRadius(Halide.Radius2)
            .Background(Halide.Alpha(Halide.Surface, 0.96))
            .BorderColor(Halide.Border).BorderWidth(1f)
            .AlignItems(AlignItems.Center)
            .AccessibleRole(AccessibilityRole.Dialog)
            .AccessibleName(UpNextIsAsking ? "Still watching?" : "Up next")
            .AutomationId("up-next");
        card.Position.Type = PositionType.Absolute;
        card.Position.Right = Dimension.Px(Halide.S5);
        card.Position.Bottom = Dimension.Px(Halide.S6 + Halide.S5);
        card.ZIndex(10);

        card.AddChild(Artwork(next, ImageKind.Primary, 142f, 80f, Halide.Radius1));

        var text = Flex.Column(gap: Halide.S1).FlexShrink(1);
        text.AddChild(Ui.Line(UpNextIsAsking ? "Still watching?" : "Up next", 11.5f, Halide.Accent300));
        text.AddChild(Ui.Line(next.Name, 14f, Halide.Text));
        var sub = next.Kind == MediaKind.Episode && next.SeriesName is { Length: > 0 } series
            ? $"{series} · {next.SubLine}"
            : next.SubLine;
        if (sub.Length > 0) text.AddChild(Ui.Line(sub, 11.5f, Halide.TextMuted));
        card.AddChild(text);

        var actions = Flex.Column(gap: Halide.S2).FlexShrink(0);
        if (UpNextIsAsking)
        {
            // No countdown here: the whole point is that nothing happens until someone
            // says so.
            actions.AddChild(Ui.Button(ButtonVariant.Primary, "Keep watching",
                ConfirmStillWatching, "up-next-continue", Icons.Play));
            actions.AddChild(Ui.Button(ButtonVariant.Ghost, "Stop",
                () => StopPlayback(report: true), "up-next-stop"));
        }
        else
        {
            actions.AddChild(Ui.Button(ButtonVariant.Primary, $"Play now ({UpNextSecondsLeft})",
                () => PlayUpNext(deliberate: true), "up-next-play", Icons.Play));
            actions.AddChild(Ui.Button(ButtonVariant.Ghost, "Not now", DismissUpNext, "up-next-dismiss"));
        }
        card.AddChild(actions);
        return card;
    }
}
