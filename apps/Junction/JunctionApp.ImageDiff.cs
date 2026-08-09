using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Rendering.Skia;
using Junction.Git;
using Junction.Theme;

namespace Junction;

public partial class JunctionApp
{
    // ── Image diffs ──────────────────────────────────────────────────────
    //
    // git reports image changes as "Binary files … differ", so the ordinary diff
    // pane has nothing to show. For known image extensions we instead fetch both
    // blobs as raw bytes, decode them, and show a before/after pair with each
    // side's dimensions and byte size.

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico",
    };

    private static bool IsImagePath(string path) =>
        path.Length > 0 && ImageExtensions.Contains(System.IO.Path.GetExtension(path));

    /// <summary>One side of an image comparison. A null <see cref="Source"/> with a
    /// zero <see cref="Bytes"/> means the file didn't exist on that side (added or
    /// deleted); a null source with bytes means it failed to decode.</summary>
    private sealed record ImageSide(IImageSource? Source, int Bytes, int Width, int Height);

    private sealed record ImageDiffData(ImageSide? Old, ImageSide? New)
    {
        public static readonly ImageDiffData Empty = new(null, null);
    }

    // Bounded AND disposing: each slot pins two decoded bitmaps, so eviction has to
    // release them rather than wait for finalization.
    private readonly KeyedAsyncLoader<string, ImageDiffData> _imageDiffLoaders =
        new(ImageDiffData.Empty, capacity: 8, onEvicted: DisposeImageDiff,
            comparer: StringComparer.Ordinal);

    /// <summary>Releases an evicted comparison's decoded bitmaps. Skia bitmaps are
    /// unmanaged memory, so waiting for finalization would let a browsing session hold
    /// far more than the cache's nominal size.</summary>
    private static void DisposeImageDiff(ImageDiffData data)
    {
        (data.Old?.Source as IDisposable)?.Dispose();
        (data.New?.Source as IDisposable)?.Dispose();
    }

    // Runs off the UI thread: git show + decode.
    private static ImageSide? MakeSide(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;
        try
        {
            var src = SkiaImageSource.FromBytes(bytes);
            return new ImageSide(src, bytes.Length, (int)src.IntrinsicSize.Width, (int)src.IntrinsicSize.Height);
        }
        catch
        {
            return new ImageSide(null, bytes.Length, 0, 0);   // undecodable (corrupt / unsupported)
        }
    }

    /// <summary>Resolves which two blobs to compare for the active view, matching
    /// what the text diff would have shown.</summary>
    private ImageDiffData LoadImageDiff(string repoPath, string path, ExternalDiffMode mode,
        string? commitHash, string? baseHash)
    {
        var repo = RepoFor(repoPath);
        byte[]? oldBytes, newBytes;
        switch (mode)
        {
            case ExternalDiffMode.Commit:
                oldBytes = repo.GetFileBytes(baseHash ?? commitHash + "^", path);
                newBytes = repo.GetFileBytes(commitHash!, path);
                break;
            case ExternalDiffMode.Staged:
                oldBytes = repo.GetFileBytes("HEAD", path);
                newBytes = repo.GetIndexFileBytes(path);
                break;
            default:
                oldBytes = repo.GetIndexFileBytes(path) ?? repo.GetFileBytes("HEAD", path);
                newBytes = repo.GetWorkingFileBytes(path);
                break;
        }
        return new ImageDiffData(MakeSide(oldBytes), MakeSide(newBytes));
    }

    /// <summary>The image pane for the selected file, or null when the current view
    /// has no image comparison to make.</summary>
    private Element? BuildImageDiffBody(string path)
    {
        if (!HasActiveRepo) return null;

        ExternalDiffMode mode;
        string? commitHash = null, baseHash = null;
        if (View == JunctionView.Commit)
        {
            if (_detailRange is { } r) { mode = ExternalDiffMode.Commit; commitHash = r.HeadHash; baseHash = r.BaseHash; }
            else if (_detailHash is { } h) { mode = ExternalDiffMode.Commit; commitHash = h; }
            else return null;
        }
        else if (View == JunctionView.Changes)
        {
            mode = _changeSelStaged ? ExternalDiffMode.Staged : ExternalDiffMode.Unstaged;
        }
        else
        {
            return null;
        }

        var key = mode == ExternalDiffMode.Commit
            ? $"{_activeRepoPath}|{baseHash ?? commitHash + "^"}..{commitHash}|{path}"
            : $"{_activeRepoPath}|{DataGen(_activeRepoPath)}|{mode}|{path}";

        var loader = _imageDiffLoaders.For(key);
        var repoPath = _activeRepoPath;
        loader.Load(key, () => LoadImageDiff(repoPath, path, mode, commitHash, baseHash));

        var data = loader.Value;
        var body = ScrollView.New().FlexGrow(1).Padding(new Thickness(14)).Background(Nocturne.Bg);

        if (loader.IsLoading && data.Old == null && data.New == null)
        {
            body.AddChild(Box.New().Padding(new Thickness(26))
                .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Children(Text.New("Loading image…").FontSize(13f).TextColor(Nocturne.On(0.45))));
            return body;
        }

        var col = Flex.Column(gap: 10);
        col.AddChild(ImageChangeSummary(data));

        var row = Flex.Row(gap: 12).AlignItems(AlignItems.Stretch);
        row.AddChild(ImageSideCard("Before", data.Old, Nocturne.DiffDelSign));
        row.AddChild(ImageSideCard("After", data.New, Nocturne.DiffAddSign));
        col.AddChild(row);

        body.AddChild(col);
        return body;
    }

    private static Element ImageChangeSummary(ImageDiffData d)
    {
        string label;
        Color color;
        if (d.Old == null && d.New != null) { label = "Image added"; color = Nocturne.AddCount; }
        else if (d.Old != null && d.New == null) { label = "Image deleted"; color = Nocturne.DelCount; }
        else if (d.Old != null && d.New != null)
        {
            bool resized = d.Old.Width != d.New.Width || d.Old.Height != d.New.Height;
            long delta = (long)d.New.Bytes - d.Old.Bytes;
            var size = delta == 0 ? "same size" : (delta > 0 ? "+" : "−") + FormatBytes(Math.Abs(delta));
            label = resized
                ? $"Image changed · {d.Old.Width}×{d.Old.Height} → {d.New.Width}×{d.New.Height} · {size}"
                : $"Image changed · {size}";
            color = Nocturne.Accent300;
        }
        else { label = "No image content"; color = Nocturne.On(0.45); }

        return Flex.Row(gap: 6).AlignItems(AlignItems.Center).Children(
            Ui.Icon(Icons.FileImage, 13f, color),
            Text.New(label).FontSize(12f).TextColor(color));
    }

    private static Element ImageSideCard(string title, ImageSide? side, Color accent)
    {
        var card = Flex.Column(gap: 8).FlexGrow(1).FlexBasis(Dimension.Px(0))
            .Padding(new Thickness(10))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusSm);

        var head = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        head.AddChild(Text.New(title.ToUpperInvariant()).FontSize(9.5f)
            .FontWeight(FontWeight.Medium).TextColor(accent).FlexGrow(1));
        if (side is { Width: > 0 })
            head.AddChild(Ui.Mono($"{side.Width}×{side.Height}", 10f, Nocturne.On(0.5)));
        card.AddChild(head);

        if (side == null)
        {
            card.AddChild(Box.New().Height(120)
                .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Background(Nocturne.DiffBlankBg).CornerRadius(Nocturne.RadiusSm)
                .Children(Text.New("(none)").FontSize(11.5f).TextColor(Nocturne.On(0.35))));
            return card;
        }

        if (side.Source == null)
        {
            card.AddChild(Box.New().Height(120)
                .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Background(Nocturne.DiffBlankBg).CornerRadius(Nocturne.RadiusSm)
                .Children(Text.New("Could not decode").FontSize(11.5f).TextColor(Nocturne.On(0.4))));
        }
        else
        {
            // Checkerboard-ish backdrop so transparent PNGs read clearly, and a
            // bounded height so a tall image doesn't push the metadata off-screen.
            card.AddChild(Box.New().Height(300)
                .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Background(Nocturne.Neutral900).CornerRadius(Nocturne.RadiusSm)
                .Padding(new Thickness(6))
                .Children(ContainedImage(side.Source)));
        }

        card.AddChild(Text.New(FormatBytes(side.Bytes)).FontSize(10.5f).TextColor(Nocturne.On(0.45)));
        return card;
    }

    // Fit is a property on ImageElement, not a builder method.
    private static ImageElement ContainedImage(IImageSource source)
    {
        var img = Image.New(source);
        img.Fit = ImageFit.Contain;
        img.Width(Dimension.Percent(100)).Height(Dimension.Percent(100));
        return img;
    }

    private static string FormatBytes(long n) =>
        n < 1024 ? $"{n} B"
        : n < 1024 * 1024 ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{n / 1024.0:0.#} KB")
        : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{n / (1024.0 * 1024):0.##} MB");
}
