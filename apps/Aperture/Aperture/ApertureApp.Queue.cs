using Aperture.Core;
using Aperture.Theme;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace Aperture;

/// <summary>
/// The queue: what Play all lines up, and what the player plays through.
///
/// <para>A queue is a list and a place in it, nothing more. It exists because "play this
/// season" and "play the next episode" are the same act from the viewer's side and
/// completely different questions for the server — the first is a list you decided on
/// once, the second is a guess the server makes each time. When a queue is running it
/// answers what comes next; when it is not, the server does (see
/// <see cref="ApertureApp.OnPlaybackEnded"/>).</para>
///
/// <para>Starting something else by hand ends the queue. That is the rule that keeps it
/// from following people around: a queue you did not ask for is a bug, not a feature.</para>
/// </summary>
public sealed partial class ApertureApp
{
    private readonly List<MediaItem> _queue = [];
    private int _queueIndex = -1;

    /// <summary>What the queue is called on screen — the season or library it came from.</summary>
    private string _queueTitle = "";

    /// <summary>True while the queue panel is up.</summary>
    private bool _queueOpen;

    internal IReadOnlyList<MediaItem> Queue => _queue;
    internal int QueueIndex => _queueIndex;
    internal bool QueueIsRunning => _queue.Count > 0 && _queueIndex >= 0;
    internal bool QueueOpen => _queueOpen;

    /// <summary>The item after the current one, or null at the end of the list.</summary>
    internal MediaItem? QueueNext =>
        QueueIsRunning && _queueIndex + 1 < _queue.Count ? _queue[_queueIndex + 1] : null;

    /// <summary>
    /// Starts a list. <paramref name="shuffle"/> reorders everything after the first item,
    /// so "shuffle" from a chosen episode still starts with that one — the alternative is a
    /// button that ignores what you clicked.
    /// </summary>
    internal void PlayQueue(IReadOnlyList<MediaItem> items, string title, int startIndex = 0, bool shuffle = false)
    {
        var playable = items.Where(i => i.IsPlayable).ToList();
        if (playable.Count == 0) return;

        var first = startIndex >= 0 && startIndex < items.Count && items[startIndex].IsPlayable
            ? items[startIndex]
            : playable[0];

        if (shuffle)
        {
            var rest = playable.Where(i => i.Id != first.Id).ToList();
            Shuffle(rest);
            playable = [first, .. rest];
        }

        _queue.Clear();
        _queue.AddRange(playable);
        _queueIndex = _queue.FindIndex(i => i.Id == first.Id);
        _queueTitle = title;

        Play(_queue[_queueIndex], _queue[_queueIndex].ResumePosition ?? TimeSpan.Zero, keepQueue: true);
    }

    /// <summary>Fisher-Yates, so every order is as likely as every other — a sort by a
    /// random key is not that, and on a short season the difference shows.</summary>
    private static void Shuffle(List<MediaItem> items)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    /// <summary>Jumps to a place in the queue — what clicking a row in the panel does.</summary>
    internal void PlayQueueAt(int index)
    {
        if (index < 0 || index >= _queue.Count) return;
        _queueIndex = index;
        CloseQueue();
        Play(_queue[index], _queue[index].ResumePosition ?? TimeSpan.Zero, keepQueue: true);
    }

    /// <summary>Advances to the next item. Returns false at the end of the list.</summary>
    internal bool AdvanceQueue()
    {
        if (QueueNext is null) return false;
        _queueIndex++;
        Play(_queue[_queueIndex], _queue[_queueIndex].ResumePosition ?? TimeSpan.Zero,
            keepQueue: true, countsAsDeliberate: false);
        return true;
    }

    /// <summary>Ends the queue without stopping what is playing — starting something else
    /// by hand, or reaching the end of the list.</summary>
    internal void ClearQueue()
    {
        if (_queue.Count == 0 && _queueIndex < 0) return;
        _queue.Clear();
        _queueIndex = -1;
        _queueTitle = "";
        _queueOpen = false;
    }

    internal void ToggleQueue()
    {
        if (!QueueIsRunning) return;
        _queueOpen = !_queueOpen;
        if (_queueOpen) _reportOpen = false;
        Rerender();
    }

    internal void CloseQueue()
    {
        if (!_queueOpen) return;
        _queueOpen = false;
        Rerender();
    }

    // ── The panel ────────────────────────────────────────────────────────

    /// <summary>
    /// What is playing and what follows, down the right-hand side — the same side as the
    /// chapter list, because they are the same kind of answer and only one can be up.
    /// </summary>
    private Element BuildQueuePanel()
    {
        var panel = Flex.Column(gap: 0)
            .Width(340)
            .Background(Halide.Surface)
            .BorderColor(Halide.Border).BorderWidth(new Thickness(0, 0, 0, 1))
            .AccessibleRole(AccessibilityRole.List)
            .AccessibleName("Queue")
            .AutomationId("queue");
        panel.Position.Type = PositionType.Absolute;
        panel.Position.Right = Dimension.Px(0);
        panel.Position.Top = Dimension.Px(0);
        panel.Box.Height = Dimension.Percent(100);
        panel.ZIndex(12);

        var head = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center).FlexShrink(0)
            .Height(56).Padding(new Thickness(0, Halide.S4, 0, Halide.S4))
            .BorderColor(Halide.Border).BorderWidth(new Thickness(0, 0, 1, 0));
        head.AddChild(Text.New("Playing next").FontSize(14f).FontWeight(FontWeight.SemiBold)
            .TextColor(Halide.Text));
        head.AddChild(Ui.Mono($"{_queueIndex + 1}/{_queue.Count}", 11.5f, Halide.TextFaint));
        head.AddChild(Box.New().FlexGrow(1));
        head.AddChild(Ui.IconButton(Icons.WinClose, "Close (Q)", CloseQueue, 12f, Halide.TextMuted, "queue-close"));
        panel.AddChild(head);

        if (_queueTitle.Length > 0)
            panel.AddChild(Ui.Line(_queueTitle, 11.5f, Halide.TextMuted)
                .Padding(new Thickness(Halide.S2, Halide.S4, Halide.S2, Halide.S4)));

        var scroll = ScrollView.Vertical().FlexGrow(1);
        var list = Flex.Column(gap: 0).Padding(new Thickness(Halide.S2));
        for (int i = 0; i < _queue.Count; i++)
        {
            int index = i;
            var item = _queue[i];
            bool playing = index == _queueIndex;

            var row = Flex.Row(gap: Halide.S3).AlignItems(AlignItems.Center)
                .Padding(new Thickness(Halide.S2))
                .CornerRadius(Halide.Radius1)
                .Cursor(CursorType.Pointer)
                .AccessibleRole(AccessibilityRole.Button)
                .AccessibleName(playing ? $"{item.Name}, playing now" : item.Name)
                .AutomationId($"queue-{index}");
            if (playing) row.Background(Halide.Tint(Halide.Accent));

            row.AddChild(Artwork(item, ImageKind.Primary, 84f, 48f, Halide.Radius1));
            var text = Flex.Column(gap: 1).FlexGrow(1).FlexShrink(1);
            text.AddChild(Ui.Line(item.Name, 12.5f, playing ? Halide.Text : Halide.TextDim));
            var sub = item.Kind == MediaKind.Episode ? item.SubLine : item.ProductionYear?.ToString() ?? "";
            if (sub.Length > 0) text.AddChild(Ui.Line(sub, 11f, Halide.TextFaint));
            row.AddChild(text);
            if (item.RunTime is { } run)
                row.AddChild(Ui.Mono(Ui.Runtime(run), 11f, Halide.TextFaint).FlexShrink(0));

            row.OnClick(_ => PlayQueueAt(index));
            if (!playing) row.HoverBg(Halide.Raised);
            list.AddChild(row);
        }
        scroll.AddChild(list);
        panel.AddChild(scroll);

        panel.On<KeyDownEvent>(e =>
        {
            if (e.Key is KeyCode.Escape or KeyCode.Q) { CloseQueue(); e.Handled = true; }
        });
        return panel;
    }
}
