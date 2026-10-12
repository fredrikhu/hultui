using HultUI.Core.Animation;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Rendering.Skia;
using HultUI.Terminal.Emulation;

namespace HultUI.Terminal;

/// <summary>
/// Pictures a program shows: Sixel, the kitty graphics protocol and iTerm2's inline images. The
/// engine decodes them and keeps one band per row a picture covers; this draws the bands.
/// </summary>
/// <remarks>
/// <para><b>Where.</b> A band is laid on the grid in cells: its pixels over the pixels a cell
/// covered when the picture was laid out (the engine's cell size, which the element sets from its
/// own), times the cell's size now. So a picture fills exactly the cells it took, row against row
/// with no seam, and stays in them when the font changes; where the cell is not a whole number of
/// pixels it is resampled by that fraction. A band is cut at the grid's right edge: a narrowing
/// resize hides part of a picture and widening shows it again.</para>
/// <para><b>Order.</b> Kitty's z decides: negative goes under the text, after the cell
/// backgrounds; anything else over it, as Sixel and iTerm2 pictures, which own their cells, do.</para>
/// <para><b>Textures.</b> One still image per picture and animation frame, made when it first
/// shows and dropped once a frame draws without it: only what is on screen is held.</para>
/// <para><b>Animation.</b> The engine owns no clock. While a picture animates, a frame clock moves
/// it on and the element draws again when a frame changed.</para>
/// </remarks>
public sealed partial class TerminalElement
{
    private sealed class CachedPicture(SkiaImageSource source, int frame)
    {
        public SkiaImageSource Source { get; } = source;
        public int Frame { get; } = frame;
        public bool Used { get; set; }
    }

    private readonly Dictionary<object, CachedPicture> _pictures = new(ReferenceEqualityComparer.Instance);

    /// <summary>Draws one row's bands of one layer: under the text (negative z) or over it.</summary>
    private void DrawPictures(IRenderContext ctx, TerminalSnapshot s, int row, float x0, float y, float scale, bool underText)
    {
        var runs = s.Pictures;
        for (int i = 0; i < runs.Count; i++)
        {
            var run = runs[i];
            if (run.Row != row || (run.ZIndex < 0) != underText) continue;
            DrawPicture(ctx, s, run, x0, y, scale);
        }
    }

    private void DrawPicture(IRenderContext ctx, TerminalSnapshot s, in PictureRun run, float x0, float y, float scale)
    {
        int visibleCols = Math.Min(run.Cols, s.Columns - run.Column);
        if (visibleCols <= 0 || run.CellWidth <= 0 || run.CellHeight <= 0) return;

        // Picture pixels per cell: the cell it was laid out against at its natural size, its
        // share of the box when stretched.
        float perCellX = run.PxPerCellX > 0 ? run.PxPerCellX : run.CellWidth;
        float perCellY = run.PxPerCellY > 0 ? run.PxPerCellY : run.CellHeight;

        float left = x0 + run.Column * _cellWidth + (float)run.OffsetX / run.CellWidth * _cellWidth;
        float top = y + (float)run.OffsetY / run.CellHeight * _cellHeight;
        float width = run.SrcWidth / perCellX * _cellWidth;
        float height = run.SrcHeight / perCellY * _cellHeight;
        float srcWidth = run.SrcWidth;

        // Cut at the grid's right edge, and at the cells the run took.
        float right = x0 + (run.Column + visibleCols) * _cellWidth;
        if (left + width > right)
        {
            if (right <= left) return;
            srcWidth *= (right - left) / width;
            width = right - left;
        }

        // Whole device pixels, as fills are: a picture's edges crisp, and bands meeting exactly.
        float l = Snap(left, scale), t = Snap(top, scale);
        var dest = new Rect(l, t, Snap(left + width, scale) - l, Snap(top + height, scale) - t);
        if (dest.Width <= 0 || dest.Height <= 0) return;

        ctx.DrawImage(SourceOf(run), dest, new Rect(run.SrcX, run.SrcY, srcWidth, run.SrcHeight));
    }

    /// <summary>The still image for a picture's current frame, made on first sight.</summary>
    private SkiaImageSource SourceOf(in PictureRun run)
    {
        if (_pictures.TryGetValue(run.Image, out var cached) && cached.Frame == run.Frame)
        {
            cached.Used = true;
            return cached.Source;
        }
        cached?.Source.Dispose();
        var source = SkiaImageSource.FromPixels(run.Pixels.Span, run.Width, run.Height, run.Width * 4,
            PixelFormat.Bgra8888, straightAlpha: true);
        _pictures[run.Image] = new CachedPicture(source, run.Frame) { Used = true };
        return source;
    }

    /// <summary>After a frame: drop the pictures it did not draw.</summary>
    private void SweepPictures()
    {
        if (_pictures.Count == 0) return;
        List<object>? gone = null;
        foreach (var (key, cached) in _pictures)
        {
            if (cached.Used) { cached.Used = false; continue; }
            cached.Source.Dispose();
            (gone ??= []).Add(key);
        }
        if (gone is not null)
            foreach (var key in gone) _pictures.Remove(key);
    }

    private void DropPictures()
    {
        foreach (var cached in _pictures.Values) cached.Source.Dispose();
        _pictures.Clear();
    }

    // ──── Animation ────

    private PictureClock? _pictureClock;

    /// <summary>Keeps a frame clock while the snapshot says a picture animates.</summary>
    private void TendPictureClock(TerminalSnapshot s)
    {
        if (s.PicturesAnimating && _pictureClock is null)
            AnimationManager.Add(_pictureClock = new PictureClock(this));
        else if (!s.PicturesAnimating) StopPictureClock();
    }

    private void StopPictureClock()
    {
        _pictureClock?.Cancel();
        _pictureClock = null;
    }

    private void OnPictureTick(float seconds)
    {
        if (_controller is not { } controller || !IsHosted) { StopPictureClock(); return; }
        if (controller.AdvanceAnimations(TimeSpan.FromSeconds(seconds))) MarkPaintDirty();
    }

    /// <summary>Hands the engine each frame's time while a picture animates: the frame clock's
    /// own step, so a headless session's virtual time drives it as a window's real time does.</summary>
    private sealed class PictureClock(TerminalElement owner) : ITween
    {
        public bool IsComplete { get; private set; }
        public bool Started { get; private set; }
        // Every step can show a new frame: the engine knows its frames' times, not this.
        public float NextChangeIn => 0f;
        public float Delay => 0f;
        public void Tick(float deltaSeconds)
        {
            Started = true;
            if (!IsComplete) owner.OnPictureTick(deltaSeconds);
        }
        public void Cancel() => IsComplete = true;
    }

    /// <summary>Whether a picture clock runs; for tests.</summary>
    internal bool PictureClockRunning => _pictureClock is not null;

    /// <summary>The pictures held as textures; for tests.</summary>
    internal int HeldPictures => _pictures.Count;
}
