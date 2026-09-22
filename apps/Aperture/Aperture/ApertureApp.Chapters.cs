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
/// Chapters: the marks the file already carries, which nothing was showing.
///
/// <para>A list over the picture (C), and the two keys that matter without opening
/// anything — Page Up and Page Down step between marks. The list is a panel rather than
/// pips on the scrubber because a two-hour film has twenty-seven of them: as pips they
/// are a row of ticks nobody can hit, and as a list they are a table of contents.</para>
/// </summary>
public sealed partial class ApertureApp
{
    /// <summary>True while the chapter panel is up.</summary>
    private bool _chaptersOpen;

    /// <summary>The chapters of whatever is playing. Empty for a file with none, which is
    /// most of television.</summary>
    internal IReadOnlyList<MediaChapter> PlayingChapters =>
        _playingItem?.Chapters ?? [];

    internal bool ChaptersOpen => _chaptersOpen;

    internal void ToggleChapters()
    {
        if (PlayingChapters.Count == 0) return;
        _chaptersOpen = !_chaptersOpen;
        Rerender();
    }

    internal void CloseChapters()
    {
        if (!_chaptersOpen) return;
        _chaptersOpen = false;
        Rerender();
    }

    /// <summary>
    /// The chapter the playhead is inside: the last one that starts at or before it.
    /// </summary>
    internal int CurrentChapterIndex(TimeSpan position)
    {
        var chapters = PlayingChapters;
        int index = -1;
        for (int i = 0; i < chapters.Count; i++)
        {
            if (chapters[i].Start <= position) index = i;
            else break;
        }
        return index;
    }

    /// <summary>
    /// Steps a chapter. Back from just inside a chapter goes to its start rather than the
    /// one before — the same rule a CD player follows, and the reason "back" twice in a row
    /// is how you actually reach the previous chapter.
    /// </summary>
    internal void StepChapter(int direction)
    {
        if (_player is not { } player) return;
        var chapters = PlayingChapters;
        if (chapters.Count == 0) return;

        var position = player.Position;
        int current = CurrentChapterIndex(position);

        int target;
        if (direction > 0)
        {
            target = current + 1;
            if (target >= chapters.Count) return;   // past the last one: stay put
        }
        else
        {
            // More than a few seconds into this chapter, "back" means its own start.
            bool restartThisOne = current >= 0 &&
                position - chapters[current].Start > TimeSpan.FromSeconds(3);
            target = restartThisOne ? current : current - 1;
            if (target < 0) target = 0;
        }

        player.Seek(chapters[target].Start);
        Flash(chapters[target].Name);
    }

    internal void SeekToChapter(int index)
    {
        if (_player is not { } player) return;
        var chapters = PlayingChapters;
        if (index < 0 || index >= chapters.Count) return;
        player.Seek(chapters[index].Start);
        CloseChapters();
    }

    // ── The panel ────────────────────────────────────────────────────────

    /// <summary>
    /// The chapter list, down the right-hand side over the picture. Keyed to the playhead:
    /// the chapter you are in is marked, so opening this mid-film answers "where am I"
    /// before it answers "where else could I be".
    /// </summary>
    private Element BuildChapterPanel()
    {
        var chapters = PlayingChapters;
        int current = CurrentChapterIndex(_player?.Position ?? TimeSpan.Zero);

        var panel = Flex.Column(gap: 0)
            .Width(320)
            .Background(Halide.Surface)
            .BorderColor(Halide.Border).BorderWidth(new Thickness(0, 0, 0, 1))
            .AccessibleRole(AccessibilityRole.List)
            .AccessibleName("Chapters")
            .AutomationId("chapters");
        panel.Position.Type = PositionType.Absolute;
        panel.Position.Right = Dimension.Px(0);
        panel.Position.Top = Dimension.Px(0);
        panel.Box.Height = Dimension.Percent(100);
        panel.ZIndex(12);

        var head = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center).FlexShrink(0)
            .Height(56).Padding(new Thickness(0, Halide.S4, 0, Halide.S4))
            .BorderColor(Halide.Border).BorderWidth(new Thickness(0, 0, 1, 0));
        head.AddChild(Text.New("Chapters").FontSize(14f).FontWeight(FontWeight.SemiBold).TextColor(Halide.Text));
        head.AddChild(Ui.Mono(chapters.Count.ToString(), 11.5f, Halide.TextFaint));
        head.AddChild(Box.New().FlexGrow(1));
        head.AddChild(Ui.IconButton(Icons.WinClose, "Close (C)", CloseChapters, 12f, Halide.TextMuted,
            "chapters-close"));
        panel.AddChild(head);

        var scroll = ScrollView.Vertical().FlexGrow(1);
        var list = Flex.Column(gap: 0).Padding(new Thickness(Halide.S2));
        for (int i = 0; i < chapters.Count; i++)
        {
            int index = i;
            var chapter = chapters[i];
            bool here = index == current;

            var row = Flex.Row(gap: Halide.S3).AlignItems(AlignItems.Center)
                .Padding(new Thickness(Halide.S2, Halide.S3, Halide.S2, Halide.S3))
                .CornerRadius(Halide.Radius1)
                .Cursor(CursorType.Pointer)
                .AccessibleRole(AccessibilityRole.Button)
                .AccessibleName(here ? $"{chapter.Name}, playing now" : chapter.Name)
                .AutomationId($"chapter-{index}");
            if (here) row.Background(Halide.Tint(Halide.Accent));

            row.AddChild(Ui.Mono(Ui.Clock(chapter.Start), 11.5f, here ? Halide.Accent300 : Halide.TextFaint)
                .FlexShrink(0));
            row.AddChild(Ui.Line(chapter.Name, 12.5f, here ? Halide.Text : Halide.TextDim).FlexGrow(1));
            row.OnClick(_ => SeekToChapter(index));
            if (!here) row.HoverBg(Halide.Raised);
            list.AddChild(row);
        }
        scroll.AddChild(list);
        panel.AddChild(scroll);

        panel.On<KeyDownEvent>(e =>
        {
            if (e.Key is KeyCode.Escape or KeyCode.C) { CloseChapters(); e.Handled = true; }
        });
        return panel;
    }
}
