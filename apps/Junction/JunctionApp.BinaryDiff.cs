using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Junction.Git;
using Junction.Theme;

namespace Junction;

public partial class JunctionApp
{
    // ── Binary diffs ─────────────────────────────────────────────────────
    //
    // git will not diff a binary file: it emits "Binary files a/x and b/x differ" and no
    // hunks at all. Images get a real before/after comparison (JunctionApp.ImageDiff.cs);
    // everything else — archives, fonts, PDFs, compiled output — gets this card, which
    // says that the file is binary and what changed about it, and shows no content.
    //
    // Nothing here reads a blob, so the syntax highlighter is never asked to tokenize
    // bytes that are not text: HighlightForCurrent lives in BuildDiffScroll, and a binary
    // file never reaches it.

    /// <summary>Byte sizes of the two sides. Null where the file did not exist on that
    /// side — which is what makes the change an add or a delete.</summary>
    private sealed record BinarySizes(long? Old, long? New)
    {
        public static readonly BinarySizes Unknown = new(null, null);
    }

    // Nothing to dispose here (two longs per slot), unlike the image cache next door,
    // so the eviction hook is left off.
    private readonly KeyedAsyncLoader<string, BinarySizes> _binaryDiffLoaders =
        new(BinarySizes.Unknown, capacity: 16, comparer: StringComparer.Ordinal);

    /// <summary>Which two blobs the active view is comparing, or null when it is comparing
    /// none (no repository open, or a view that has no diff of its own).</summary>
    private (ExternalDiffMode Mode, string? CommitHash, string? BaseHash)? CurrentDiffSource()
    {
        if (!HasActiveRepo) return null;
        if (View == JunctionView.Commit)
        {
            // Multi-select compare: oldest selected commit on the left, newest on the
            // right, matching what GetRangeDiff would have produced.
            if (_detailRange is { } r) return (ExternalDiffMode.Commit, r.HeadHash, r.BaseHash);
            if (_detailHash is { } h) return (ExternalDiffMode.Commit, h, null);
            return null;
        }
        if (View == JunctionView.Changes)
            return (_changeSelStaged ? ExternalDiffMode.Staged : ExternalDiffMode.Unstaged, null, null);
        return null;
    }

    /// <summary>Cache key for one file's comparison. Commit content is immutable, so it is
    /// content-addressed (with the old ref spelled out, or a single-commit view and a range
    /// ending at the same commit would collide); working and index content moves with the
    /// repository's data generation, so stale entries die with the gen bump.</summary>
    private string DiffSourceKey(ExternalDiffMode mode, string? commitHash, string? baseHash, string path) =>
        mode == ExternalDiffMode.Commit
            ? $"{_activeRepoPath}|{baseHash ?? commitHash + "^"}..{commitHash}|{path}"
            : $"{_activeRepoPath}|{DataGen(_activeRepoPath)}|{mode}|{path}";

    // Runs off the UI thread: three git subprocesses at worst, and no blob is fetched.
    private BinarySizes LoadBinarySizes(string repoPath, string path, ExternalDiffMode mode,
        string? commitHash, string? baseHash)
    {
        var repo = RepoFor(repoPath);
        return mode switch
        {
            ExternalDiffMode.Commit => new BinarySizes(
                repo.GetBlobSize(baseHash ?? commitHash + "^", path),
                repo.GetBlobSize(commitHash!, path)),
            ExternalDiffMode.Staged => new BinarySizes(
                repo.GetBlobSize("HEAD", path),
                repo.GetIndexBlobSize(path)),
            // Unstaged: the index is the "before" when the file is staged-then-modified,
            // HEAD when it isn't, and neither when the file is untracked.
            _ => new BinarySizes(
                repo.GetIndexBlobSize(path) ?? repo.GetBlobSize("HEAD", path),
                repo.GetWorkingFileSize(path)),
        };
    }

    /// <summary>The summary card for a file git refused to diff: that it is binary, how it
    /// changed, both byte sizes and the delta, and the blob hashes. No content, by design.</summary>
    private Element BuildBinaryDiffBody(string path, GitBinaryDiff info)
    {
        var sizes = BinarySizes.Unknown;
        bool loading = false;
        if (CurrentDiffSource() is { } src)
        {
            var key = DiffSourceKey(src.Mode, src.CommitHash, src.BaseHash, path);
            var loader = _binaryDiffLoaders.For(key);
            var repoPath = _activeRepoPath;
            loader.Load(key, () => LoadBinarySizes(repoPath, path, src.Mode, src.CommitHash, src.BaseHash));
            sizes = loader.Value;
            loading = loader.IsLoading;
        }

        var (kind, kindColor) = info.Kind switch
        {
            FileChangeKind.Added => ("added", Nocturne.AddCount),
            FileChangeKind.Deleted => ("deleted", Nocturne.DelCount),
            FileChangeKind.Renamed => ("renamed", Nocturne.Accent300),
            _ => ("modified", Nocturne.Accent300),
        };

        var card = Flex.Column(gap: 12).AlignItems(AlignItems.Center).FlexShrink(0)
            .Padding(new Thickness(20, 24, 20, 24))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusMd);

        // Same words as the Files browser's binary pane, so the two surfaces read as one
        // product rather than two people's idea of the same message.
        card.AddChild(Flex.Row(gap: 7).AlignItems(AlignItems.Center).Children(
            Ui.Icon(Icons.FileBinary, 15f, kindColor),
            Text.New($"Binary file · {kind}").FontSize(13f)
                .FontWeight(FontWeight.Medium).TextColor(kindColor).MaxLines(1)));
        card.AddChild(Text.New("Its contents are not shown — git reports only that the two versions differ.")
            .FontSize(11f).TextColor(Nocturne.On(0.42)));

        string? placeholder = loading ? "…" : null;
        var facts = Flex.Column(gap: 4).FlexShrink(0);
        facts.AddChild(BinaryFactRow("BEFORE", Sized(sizes.Old, placeholder), info.OldBlob, Nocturne.DiffDelSign));
        facts.AddChild(BinaryFactRow("AFTER", Sized(sizes.New, placeholder), info.NewBlob, Nocturne.DiffAddSign));
        if (sizes.Old is { } before && sizes.New is { } after)
        {
            long delta = after - before;
            // Same vocabulary as the image comparison's summary line.
            var text = delta == 0 ? "same size" : (delta > 0 ? "+" : "−") + FormatBytes(Math.Abs(delta));
            facts.AddChild(BinaryFactRow("CHANGE", text, null, Nocturne.On(0.42)));
        }
        card.AddChild(facts);

        // Box.New() is a column, so JustifyContent centres vertically and AlignItems
        // horizontally — the card sits in the middle of whatever the pane gives it.
        var body = Box.New().FlexGrow(1)
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Padding(new Thickness(14))
            .Background(Nocturne.Bg);
        body.AddChild(card);
        // Keyed, and distinctly from the diff list's "diff:<path>": this element and the
        // scrolling text diff take the same slot under the header, and a keyless swap
        // would be patched rather than replaced — inheriting the list's handlers.
        body.Key = "diff-binary:" + path;
        return body;
    }

    private static string Sized(long? bytes, string? placeholder) =>
        bytes is { } n ? FormatBytes(n) : placeholder ?? "—";

    private static Element BinaryFactRow(string label, string value, string? blob, Color labelColor) =>
        Flex.Row(gap: 10).AlignItems(AlignItems.Center).Children(
            Text.New(label).FontSize(9.5f).FontWeight(FontWeight.Medium).TextColor(labelColor)
                .Width(56).FlexShrink(0).MaxLines(1).OverflowMode(TextOverflow.Ellipsis),
            Ui.Mono(value, 11.5f, Nocturne.On(0.72))
                .Width(78).FlexShrink(0).MaxLines(1).OverflowMode(TextOverflow.Ellipsis),
            Ui.Mono(blob ?? "", 11f, Nocturne.On(0.34))
                .FlexShrink(1).MaxLines(1).OverflowMode(TextOverflow.Ellipsis));
}
