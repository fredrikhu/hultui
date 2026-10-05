using System.Runtime.CompilerServices;
using SkiaSharp;

namespace HultUI.Headless;

/// <summary>
/// Golden-image testing: a frame compared against a PNG committed next to the test.
/// <para>
/// Create the session with <see cref="HeadlessOptions.Golden"/>, so text draws in the bundled
/// fonts as outlines and the frame comes out the same on Windows and Linux, then call
/// <see cref="Match(HeadlessWindow, string, GoldenTolerance?, string)"/>. The baseline is
/// <c>Golden/&lt;name&gt;.png</c> beside the calling source file.
/// </para>
/// <para>
/// With <see cref="UpdateVariable"/> set to <c>1</c>, a missing or different baseline is
/// written from the frame instead of failing — how baselines are made and how an intended
/// change is accepted. Review the PNGs in the diff before committing them. On a mismatch, the
/// frame, the baseline and a diff (differing pixels red over a faded baseline) are written to
/// <see cref="FailureDirectory"/>.
/// </para>
/// </summary>
public static class GoldenImage
{
    /// <summary>Set to <c>1</c> (or <c>true</c>) to write baselines rather than compare against them.</summary>
    public const string UpdateVariable = "HULTUI_UPDATE_GOLDENS";

    /// <summary>Overrides where a mismatch's images are written.</summary>
    public const string FailureDirectoryVariable = "HULTUI_GOLDEN_FAILURES";

    /// <summary>The folder, beside the test's source file, baselines live in.</summary>
    public const string BaselineFolder = "Golden";

    // Compared and stored unpremultiplied: what a PNG holds, so a baseline written from a
    // frame reads back as exactly that frame.
    private static readonly SKImageInfo s_format = new(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul);

    /// <summary>Where a mismatch's actual, expected and diff images go:
    /// <see cref="FailureDirectoryVariable"/>, or <c>golden-failures</c> under the test output.</summary>
    public static string FailureDirectory =>
        Environment.GetEnvironmentVariable(FailureDirectoryVariable) is { Length: > 0 } dir
            ? dir
            : Path.Combine(AppContext.BaseDirectory, "golden-failures");

    /// <summary>Whether <see cref="UpdateVariable"/> asks for baselines to be written.</summary>
    public static bool Updating =>
        Environment.GetEnvironmentVariable(UpdateVariable) is "1" or "true" or "TRUE" or "True";

    /// <summary>
    /// Renders a fresh frame of <paramref name="window"/> and compares it against the baseline
    /// <paramref name="name"/>. Throws <see cref="GoldenImageMismatchException"/> when it differs by
    /// more than <paramref name="tolerance"/> (default <see cref="GoldenTolerance.Default"/>), or
    /// when there is no baseline yet.
    /// </summary>
    /// <exception cref="InvalidOperationException">The session was not created with
    /// <see cref="HeadlessOptions.Golden"/>: its text would come from the machine's fonts, and a
    /// baseline made on one OS would fail on the other.</exception>
    public static void Match(HeadlessWindow window, string name, GoldenTolerance? tolerance = null,
        [CallerFilePath] string callerFilePath = "")
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!window.PinnedText)
            throw new InvalidOperationException(
                "Golden images need pinned text: create the session with HeadlessOptions.Golden() " +
                "(or PinnedText = true), or a baseline made on one OS fails on another.");
        using var image = window.CapturePixels();
        Match(image, name, tolerance, callerFilePath);
    }

    /// <summary>Compares <paramref name="actual"/> against the baseline <paramref name="name"/> —
    /// for an image drawn some other way than a headless window's frame.</summary>
    public static void Match(SKImage actual, string name, GoldenTolerance? tolerance = null,
        [CallerFilePath] string callerFilePath = "")
    {
        ArgumentNullException.ThrowIfNull(actual);
        Match(actual, name, BaselineDirectoryFor(callerFilePath), tolerance ?? GoldenTolerance.Default);
    }

    /// <summary>The comparison with the baseline folder given rather than found from the caller.</summary>
    internal static void Match(SKImage actual, string name, string baselineDirectory, GoldenTolerance tolerance)
    {
        ValidateName(name);
        string baselinePath = Path.Combine(baselineDirectory, name + ".png");
        using var actualPixels = ReadPixels(actual);

        if (!File.Exists(baselinePath))
        {
            if (Updating)
            {
                WritePng(actualPixels, baselinePath);
                return;
            }
            string written = WriteFailure(name, "actual", actualPixels);
            throw new GoldenImageMismatchException(
                $"Golden image '{name}' has no baseline at {baselinePath}. Set {UpdateVariable}=1 and run " +
                $"again to create it from this frame, which is at {written}.",
                comparison: null, baselinePath, written, expectedPath: null, diffPath: null);
        }

        using var expectedPixels = DecodePng(baselinePath);
        var comparison = Compare(expectedPixels, actualPixels, tolerance);
        if (comparison.Passed)
        {
            DeleteFailures(name);
            return;
        }
        if (Updating)
        {
            WritePng(actualPixels, baselinePath);
            DeleteFailures(name);
            return;
        }

        string actualPath = WriteFailure(name, "actual", actualPixels);
        string expectedPath = WriteFailure(name, "expected", expectedPixels);
        string? diffPath = null;
        if (!comparison.SizeDiffers)
        {
            using var diff = DiffImage(expectedPixels, actualPixels, tolerance.Channel);
            diffPath = WriteFailure(name, "diff", diff);
        }
        throw new GoldenImageMismatchException(
            $"Golden image '{name}' {comparison.Describe(tolerance)}.\n" +
            $"  expected: {expectedPath}\n  actual:   {actualPath}\n" +
            (diffPath is null ? "" : $"  diff:     {diffPath}\n") +
            $"If the change is intended, set {UpdateVariable}=1, run again, and review the new baseline.",
            comparison, baselinePath, actualPath, expectedPath, diffPath);
    }

    /// <summary>
    /// Compares two images pixel by pixel: a pixel differs when any channel (alpha included,
    /// unpremultiplied) is further apart than <see cref="GoldenTolerance.Channel"/>.
    /// </summary>
    public static GoldenComparison Compare(SKImage expected, SKImage actual, GoldenTolerance? tolerance = null)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        using var e = ReadPixels(expected);
        using var a = ReadPixels(actual);
        return Compare(e, a, tolerance ?? GoldenTolerance.Default);
    }

    private static GoldenComparison Compare(SKBitmap expected, SKBitmap actual, GoldenTolerance tolerance)
    {
        if (expected.Width != actual.Width || expected.Height != actual.Height)
            return new GoldenComparison(expected.Width, expected.Height, actual.Width, actual.Height,
                DifferentPixels: -1, MaxChannelDelta: 0, default, tolerance.Pixels);

        var e = Bytes(expected);
        var a = Bytes(actual);
        int width = expected.Width, height = expected.Height;
        int different = 0, maxDelta = 0;
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        for (int y = 0; y < height; y++)
        {
            int row = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                int i = row + x * 4;
                int delta = MaxDelta(e, a, i);
                if (delta > maxDelta) maxDelta = delta;
                if (delta <= tolerance.Channel) continue;
                different++;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
        }
        var bounds = different == 0 ? default : SKRectI.Create(left, top, right - left + 1, bottom - top + 1);
        return new GoldenComparison(width, height, width, height, different, maxDelta, bounds, tolerance.Pixels);
    }

    private static int MaxDelta(ReadOnlySpan<byte> e, ReadOnlySpan<byte> a, int i) =>
        Math.Max(Math.Max(Math.Abs(e[i] - a[i]), Math.Abs(e[i + 1] - a[i + 1])),
                 Math.Max(Math.Abs(e[i + 2] - a[i + 2]), Math.Abs(e[i + 3] - a[i + 3])));

    /// <summary>Differing pixels in red over a faded grey copy of the baseline, so where the
    /// change is reads at a glance; pixels within tolerance but not equal in pale orange.</summary>
    private static SKBitmap DiffImage(SKBitmap expected, SKBitmap actual, int channelTolerance)
    {
        var diff = new SKBitmap(s_format.WithSize(expected.Width, expected.Height));
        var e = Bytes(expected);
        var a = Bytes(actual);
        var d = diff.GetPixelSpan();
        for (int i = 0; i < e.Length; i += 4)
        {
            int delta = MaxDelta(e, a, i);
            if (delta > channelTolerance)
            {
                d[i] = 255; d[i + 1] = 0; d[i + 2] = 0;
            }
            else if (delta > 0)
            {
                d[i] = 255; d[i + 1] = 200; d[i + 2] = 140;
            }
            else
            {
                // Luma of the baseline over white, pressed into the top third of the range.
                int luma = (e[i] * 77 + e[i + 1] * 150 + e[i + 2] * 29) >> 8;
                int over = (luma * e[i + 3] + 255 * (255 - e[i + 3])) / 255;
                byte grey = (byte)(170 + over / 3);
                d[i] = grey; d[i + 1] = grey; d[i + 2] = grey;
            }
            d[i + 3] = 255;
        }
        return diff;
    }

    private static ReadOnlySpan<byte> Bytes(SKBitmap bitmap) => bitmap.GetPixelSpan();

    private static SKBitmap ReadPixels(SKImage image)
    {
        var bitmap = new SKBitmap(s_format.WithSize(image.Width, image.Height));
        if (!image.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0))
        {
            bitmap.Dispose();
            throw new InvalidOperationException("Could not read the image's pixels.");
        }
        return bitmap;
    }

    private static SKBitmap DecodePng(string path)
    {
        using var codec = SKCodec.Create(path)
            ?? throw new InvalidOperationException($"The baseline {path} is not an image Skia can read.");
        var bitmap = new SKBitmap(s_format.WithSize(codec.Info.Width, codec.Info.Height));
        var result = codec.GetPixels(bitmap.Info, bitmap.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            bitmap.Dispose();
            throw new InvalidOperationException($"Could not decode the baseline {path}: {result}.");
        }
        return bitmap;
    }

    private static void WritePng(SKBitmap bitmap, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("Could not encode the image as PNG.");
        using var file = File.Create(path);
        data.SaveTo(file);
    }

    private static string WriteFailure(string name, string kind, SKBitmap bitmap)
    {
        string path = Path.Combine(FailureDirectory, $"{name}.{kind}.png");
        WritePng(bitmap, path);
        return path;
    }

    // A pass clears what an earlier failure of the same image left, so the folder only ever
    // holds what is failing now.
    private static void DeleteFailures(string name)
    {
        foreach (var kind in new[] { "actual", "expected", "diff" })
        {
            string path = Path.Combine(FailureDirectory, $"{name}.{kind}.png");
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static string BaselineDirectoryFor(string callerFilePath)
    {
        string? dir = string.IsNullOrEmpty(callerFilePath) ? null : Path.GetDirectoryName(callerFilePath);
        if (dir is null || !Directory.Exists(dir))
            throw new InvalidOperationException(
                $"Cannot find the test's source folder from '{callerFilePath}', which baselines live beside. " +
                "Golden images need the source tree on the machine running the tests, built without a " +
                "PathMap that rewrites source paths (ContinuousIntegrationBuild does).");
        return Path.Combine(dir, BaselineFolder);
    }

    // One portable set of characters: a name that is a file on Linux must be one on Windows.
    private static void ValidateName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')) ||
            name.StartsWith('.'))
            throw new ArgumentException(
                $"'{name}' is not a golden image name: use letters, digits, '-', '_' and '.', not starting with '.'.",
                nameof(name));
    }
}

/// <summary>
/// How far a frame may stray from its baseline: <paramref name="Channel"/> is how far apart any
/// one channel (0–255) of a pixel may be before the pixel counts as different, and
/// <paramref name="Pixels"/> how many different pixels pass anyway.
/// </summary>
public sealed record GoldenTolerance(int Channel = 2, int Pixels = 0)
{
    /// <summary>Every pixel identical.</summary>
    public static GoldenTolerance Exact { get; } = new(0, 0);

    /// <summary>
    /// A channel may be 2 apart, no pixel beyond: room for Skia's SIMD paths rounding a blend
    /// differently on another CPU, none for a moved edge or a changed colour, which shift
    /// channels by far more.
    /// </summary>
    public static GoldenTolerance Default { get; } = new();
}

/// <summary>The outcome of comparing a frame with its baseline.</summary>
/// <param name="ExpectedWidth">The baseline's width in pixels.</param>
/// <param name="ExpectedHeight">The baseline's height in pixels.</param>
/// <param name="ActualWidth">The frame's width in pixels.</param>
/// <param name="ActualHeight">The frame's height in pixels.</param>
/// <param name="DifferentPixels">Pixels beyond the channel tolerance; -1 when the sizes differ.</param>
/// <param name="MaxChannelDelta">The largest difference in any channel of any pixel.</param>
/// <param name="Bounds">The rectangle around every different pixel; empty when there are none.</param>
/// <param name="AllowedPixels">How many different pixels the tolerance let pass.</param>
public sealed record GoldenComparison(
    int ExpectedWidth, int ExpectedHeight, int ActualWidth, int ActualHeight,
    int DifferentPixels, int MaxChannelDelta, SKRectI Bounds, int AllowedPixels)
{
    /// <summary>Whether the images are different sizes, which no tolerance passes.</summary>
    public bool SizeDiffers => DifferentPixels < 0;

    /// <summary>Whether the frame matches its baseline within the tolerance.</summary>
    public bool Passed => !SizeDiffers && DifferentPixels <= AllowedPixels;

    internal string Describe(GoldenTolerance tolerance) => SizeDiffers
        ? $"is {ActualWidth}×{ActualHeight}, its baseline {ExpectedWidth}×{ExpectedHeight}"
        : $"differs in {DifferentPixels} pixel{(DifferentPixels == 1 ? "" : "s")} by more than ±{tolerance.Channel} " +
          $"({AllowedPixels} allowed), at most {MaxChannelDelta}, within x {Bounds.Left}–{Bounds.Right - 1}, " +
          $"y {Bounds.Top}–{Bounds.Bottom - 1}";
}

/// <summary>A frame that does not match its golden image, or has none.</summary>
public sealed class GoldenImageMismatchException : Exception
{
    internal GoldenImageMismatchException(string message, GoldenComparison? comparison, string baselinePath,
        string actualPath, string? expectedPath, string? diffPath) : base(message)
    {
        Comparison = comparison;
        BaselinePath = baselinePath;
        ActualPath = actualPath;
        ExpectedPath = expectedPath;
        DiffPath = diffPath;
    }

    /// <summary>The comparison, or null when there was no baseline to compare with.</summary>
    public GoldenComparison? Comparison { get; }

    /// <summary>Where the baseline is, or would be.</summary>
    public string BaselinePath { get; }

    /// <summary>The frame, as written to the failure folder.</summary>
    public string ActualPath { get; }

    /// <summary>A copy of the baseline in the failure folder, beside the frame.</summary>
    public string? ExpectedPath { get; }

    /// <summary>The diff image, or null when the sizes differ.</summary>
    public string? DiffPath { get; }
}
