using Aperture.Core;
using Aperture.Theme;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using HultUI.Media;

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
        if (_chaptersOpen) _reportOpen = false;
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
    /// The chapter the playhead is in, for the panel to mark — as a value a build can depend
    /// on. The panel read <see cref="MediaPlayer.Position"/> in the app's build, so while it was
    /// open every position report (ten a second while playing) rebuilt the whole app; this
    /// changes only when the playhead crosses into another chapter, and a build that reads it
    /// runs again only then. Made again for each player and for each item the chapters come
    /// from (the full item, with its marks, arrives a moment after playback starts).
    /// </summary>
    private int ChapterHere()
    {
        if (_player is not { } player) return -1;
        if (!ReferenceEquals(_chapterHereOf, player) || !ReferenceEquals(_chapterHereItem, _playingItem))
        {
            _chapterHereOf = player;
            _chapterHereItem = _playingItem;
            _chapterHere = new Computed<int>(() => CurrentChapterIndex(player.Position));
        }
        return _chapterHere!.Value;
    }

    private Computed<int>? _chapterHere;
    private MediaPlayer? _chapterHereOf;
    private MediaItem? _chapterHereItem;

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

    /// <summary>How wide a chapter's still is drawn in the list. 16:9, so the row is
    /// 45 points tall — a thumbnail, not a contact sheet.</summary>
    private const float ChapterThumbWidth = 80f;

    /// <summary>
    /// The still beside a chapter, or null when the server has not extracted one. Nothing
    /// is drawn in its place: a column of grey boxes beside the marks of a file that never
    /// got images is worse than a plain list, which is what this was before.
    /// </summary>
    private Element? ChapterImage(int index, MediaChapter chapter)
    {
        if (_library is not { } lib || _playingItem is not { } item) return null;
        if (lib.ChapterImageUrl(item.Id, index, chapter.ImageTag, ArtworkPixels(ChapterThumbWidth)) is not { } url)
            return null;

        var (image, status) = _posters.Get(url, StandInPixels(ChapterThumbWidth) is { } old
            ? lib.ChapterImageUrl(item.Id, index, chapter.ImageTag, old) : null);
        if (status != PosterCache.Status.Ready || image is not { IsLoaded: true }) return null;

        var box = Box.New()
            .Width(ChapterThumbWidth).Height(ChapterThumbWidth * 9f / 16f).FlexShrink(0)
            .CornerRadius(Halide.Radius1)
            .Background(Halide.Raised)
            .AccessibleRole(AccessibilityRole.Image)
            .AccessibleName($"{chapter.Name}, at {Ui.Clock(chapter.Start)}")
            .AutomationId($"chapter-{index}-still");
        box.Box.OverflowX = Overflow.Hidden;
        box.Box.OverflowY = Overflow.Hidden;
        var picture = Image.New(image);
        picture.Fit = ImageFit.Cover;
        box.AddChild(picture.Size(Dimension.Percent(100), Dimension.Percent(100)));
        return box;
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
        int current = ChapterHere();

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

            // The picture the server extracted for the mark, when it has one. A chapter
            // named "Chapter 4" says nothing; the frame it starts on says what it is.
            if (ChapterImage(index, chapter) is { } still) row.AddChild(still);

            var text = Flex.Column(gap: 1).FlexGrow(1).FlexShrink(1);
            text.AddChild(Ui.Line(chapter.Name, 12.5f, here ? Halide.Text : Halide.TextDim));
            text.AddChild(Ui.Mono(Ui.Clock(chapter.Start), 11f, here ? Halide.Accent300 : Halide.TextFaint));
            row.AddChild(text);
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
