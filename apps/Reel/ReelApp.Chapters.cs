using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Media;

namespace Reel;

/// <summary>
/// Moving by chapter, for the files that have them — which for a ripped disc is most of
/// them, and for a download is almost none.
///
/// <para>The marks are the container's own, read by mpv and already cutting the scrubber
/// into segments. This adds the two things a segmented bar cannot do by itself: jumping
/// between marks, and a list saying what they are.</para>
///
/// <para>Aperture's panel puts a still beside each row, because a server has already
/// extracted one; a file on disk has no such picture, so the rows here are a name and a
/// time. That is not a placeholder for a missing feature — "Chapter 4 · 18:22" is what a
/// disc actually names its marks, and a still would have to be generated for it to say
/// more.</para>
/// </summary>
public sealed partial class ReelApp
{
    private bool _chaptersOpen;

    /// <summary>The marks of whatever is open, empty for a file with none.</summary>
    internal IReadOnlyList<MediaChapterMark> Chapters => _player?.Chapters ?? [];

    internal bool ChaptersOpen => _chaptersOpen;

    internal void ToggleChapters()
    {
        if (Chapters.Count == 0) return;
        _chaptersOpen = !_chaptersOpen;
        Rerender();
    }

    internal void CloseChapters()
    {
        if (!_chaptersOpen) return;
        _chaptersOpen = false;
        Rerender();
    }

    /// <summary>The chapter the playhead is inside: the last one starting at or before it.
    /// −1 before the first, which a file whose first mark is not at zero can produce.</summary>
    internal int CurrentChapterIndex(TimeSpan position)
    {
        var chapters = Chapters;
        int index = -1;
        for (int i = 0; i < chapters.Count; i++)
        {
            if (chapters[i].Start <= position) index = i;
            else break;
        }
        return index;
    }

    /// <summary>
    /// Steps a chapter. Back from just inside one goes to its own start rather than to the
    /// one before — the rule every CD player has followed since 1982, and the reason
    /// pressing back twice is how you actually reach the previous chapter.
    /// </summary>
    internal bool StepChapter(int direction)
    {
        if (_player is not { } player) return false;
        var chapters = Chapters;
        if (chapters.Count == 0) return false;

        var position = player.Position;
        int current = CurrentChapterIndex(position);

        int target;
        if (direction > 0)
        {
            target = current + 1;
            if (target >= chapters.Count) return false;   // past the last one: let the file step
        }
        else
        {
            bool restartThisOne = current >= 0 &&
                position - chapters[current].Start > TimeSpan.FromSeconds(3);
            target = restartThisOne ? current : current - 1;
            if (target < 0)
            {
                // Already at the top of the first chapter: nothing to step back to here, so
                // the keypress belongs to the previous file instead.
                if (!restartThisOne && current <= 0 && position <= TimeSpan.FromSeconds(3)) return false;
                target = 0;
            }
        }

        player.Seek(chapters[target].Start);
        Rerender();
        return true;
    }

    internal void SeekToChapter(int index)
    {
        if (_player is not { } player) return;
        var chapters = Chapters;
        if (index < 0 || index >= chapters.Count) return;
        player.Seek(chapters[index].Start);
        CloseChapters();
    }

    /// <summary>What a mark is called when the container did not name it. A disc that names
    /// every chapter "Chapter 4" has said nothing the row number does not already say, but
    /// an unnamed mark has to be called something.</summary>
    private static string NameOf(MediaChapterMark chapter, int index) =>
        string.IsNullOrWhiteSpace(chapter.Title) ? $"Chapter {index + 1}" : chapter.Title!;

    /// <summary>h:mm:ss, dropping the hour for anything under one.</summary>
    internal static string Clock(TimeSpan time) =>
        time < TimeSpan.FromHours(1)
            ? $"{(int)time.TotalMinutes}:{time.Seconds:00}"
            : $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}";

    private Element BuildChapterPanel()
    {
        var chapters = Chapters;
        int current = CurrentChapterIndex(_player?.Position ?? TimeSpan.Zero);

        var panel = Flex.Column(gap: 0)
            .Width(300)
            .Background(Emulsion.Surface)
            .BorderColor(Emulsion.Border).BorderWidth(new Thickness(0, 0, 0, 1))
            .AccessibleRole(AccessibilityRole.List)
            .AccessibleName("Chapters")
            .AutomationId("chapters");
        panel.Position.Type = PositionType.Absolute;
        panel.Position.Right = Dimension.Px(0);
        panel.Position.Top = Dimension.Px(0);
        panel.Box.Height = Dimension.Percent(100);
        panel.ZIndex(12);

        var head = Flex.Row(gap: Emulsion.S2).AlignItems(AlignItems.Center).FlexShrink(0)
            .Height(52).Padding(new Thickness(0, Emulsion.S4, 0, Emulsion.S4))
            .BorderColor(Emulsion.Border).BorderWidth(new Thickness(0, 0, 1, 0));
        head.AddChild(Text.New("Chapters")
            .FontSize(14f).FontWeight(FontWeight.SemiBold).TextColor(Emulsion.Text));
        head.AddChild(Text.New(chapters.Count.ToString())
            .FontSize(11.5f).TextColor(Emulsion.TextFaint));
        head.AddChild(Box.New().FlexGrow(1));
        head.AddChild(Button.New("Close")
            .Variant(ButtonVariant.Secondary)
            .AccessibleName("Close chapters (C)")
            .AutomationId("chapters-close")
            .OnClick(_ => CloseChapters()));
        panel.AddChild(head);

        var scroll = ScrollView.Vertical().FlexGrow(1);
        var list = Flex.Column(gap: 0).Padding(new Thickness(Emulsion.S2));
        for (int i = 0; i < chapters.Count; i++)
        {
            int index = i;
            var chapter = chapters[i];
            bool here = index == current;
            string name = NameOf(chapter, index);

            var row = Flex.Row(gap: Emulsion.S3).AlignItems(AlignItems.Center)
                .Padding(new Thickness(Emulsion.S2, Emulsion.S3, Emulsion.S2, Emulsion.S3))
                .CornerRadius(Emulsion.Radius)
                .Cursor(CursorType.Pointer)
                .AccessibleRole(AccessibilityRole.Button)
                .AccessibleName(here ? $"{name}, playing now" : name)
                .AutomationId($"chapter-{index}");
            if (here) row.Background(Emulsion.AccentTint);

            var text = Flex.Column(gap: 1).FlexGrow(1).FlexShrink(1);
            text.AddChild(Text.New(name).FontSize(12.5f).MaxLines(1).TextOverflow(TextOverflow.Ellipsis)
                .TextColor(here ? Emulsion.Text : Emulsion.TextDim));
            text.AddChild(Text.New(Clock(chapter.Start)).FontSize(11f)
                .TextColor(here ? Emulsion.Accent : Emulsion.TextFaint));
            row.AddChild(text);
            row.OnClick(_ => SeekToChapter(index));
            if (!here) row.OnHover(b => b.BackgroundColor = Emulsion.Raised);
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
