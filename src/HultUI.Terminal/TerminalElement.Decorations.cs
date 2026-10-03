using HultUI.Core.Animation;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Terminal.Emulation;

namespace HultUI.Terminal;

/// <summary>
/// Lines drawn over the text: the five underline styles in any colour (SGR 4:1–4:5 and 58), the
/// overline, and blinking (SGR 5 and the cursor's DECSCUSR blink).
/// </summary>
/// <remarks>
/// <para><b>Underlines</b> are geometry, not the text's decoration, which has one style in the
/// text's colour. They sit where the face says (<see cref="FontMetrics.UnderlinePosition"/>), as
/// thick as it says in whole device pixels, and their patterns are laid on the grid, not on each
/// run, so a dotted underline split by a colour change does not restart its dots.</para>
/// <para><b>Blinking</b> runs on one clock, a tween on the frame loop's time, so it advances
/// in a headless session's virtual time and costs one frame per phase rather than a frame a
/// tick. It runs only while something blinks: a focused blinking cursor, or blinking text on
/// the screen. Typing shows the cursor at once.</para>
/// </remarks>
public sealed partial class TerminalElement
{
    /// <summary>Half a blink: Windows' caret rate.</summary>
    internal static readonly TimeSpan BlinkInterval = TimeSpan.FromMilliseconds(530);

    private TweenHandle _blinkTimer;
    private bool _blinkRunning;
    private bool _blinkOn = true;
    private bool _sawBlinkingText;

    internal bool BlinkClockRunning => _blinkRunning;

    // ──── Underlines and the overline ────

    private void DrawDecorations(IRenderContext ctx, TerminalSnapshot s, int row, float left, float y, float scale)
    {
        var cells = s.Row(row);
        float px = 1f / scale;
        float t = MathF.Max(1f, MathF.Round(_font.UnderlineThickness * scale)) * px;
        float bottom = y + _cellHeight;
        float baseline = y + _font.Ascent;
        float underlineY = MathF.Min(Snap(baseline + _font.UnderlinePosition, scale), bottom - t);

        int start = -1;
        (TerminalUnderline Style, Color Color) run = default;
        for (int col = 0; col <= cells.Length; col++)
        {
            (TerminalUnderline, Color)? here = null;
            if (col < cells.Length)
            {
                ref readonly var cell = ref cells[col];
                if ((cell.Flags & CellFlags.Blink) != 0) _sawBlinkingText = true;
                bool hidden = (cell.Flags & CellFlags.Blink) != 0 && !_blinkOn;
                if (cell.Underline != TerminalUnderline.None && !hidden)
                    here = (cell.Underline, Rgb(cell.UnderlineColor >= 0 ? cell.UnderlineColor : cell.Foreground,
                        (cell.Flags & CellFlags.Dim) != 0 ? (byte)128 : (byte)255));
                if ((cell.Flags & CellFlags.Overline) != 0 && !hidden)
                {
                    var r = Span(left, y, col, col + Math.Max(1, (int)cell.Width), scale);
                    ctx.DrawRect(new Rect(r.X, y, r.Width, t), Rgb(cell.Foreground));
                }
            }

            if (start >= 0 && (here is null || here.Value != run))
            {
                DrawUnderline(ctx, run.Style, run.Color, left, start, col, underlineY, t, bottom, baseline, scale);
                start = -1;
            }
            if (here is { } h && start < 0)
            {
                start = col;
                run = h;
            }
        }

        DrawLinkUnderline(ctx, s, row, left, y, underlineY, t, scale);
    }

    /// <summary>One underline under columns [from, to) of a row.</summary>
    private void DrawUnderline(IRenderContext ctx, TerminalUnderline style, Color color, float left, int from, int to,
        float y, float t, float bottom, float baseline, float scale)
    {
        float x0 = Snap(left + from * _cellWidth, scale), x1 = Snap(left + to * _cellWidth, scale);
        if (x1 <= x0) return;

        switch (style)
        {
            case TerminalUnderline.Double:
            {
                // Two strokes a stroke apart, lifted when the second would leave the cell.
                float first = y, second = y + 2 * t;
                if (second + t > bottom)
                {
                    second = bottom - t;
                    first = MathF.Max(baseline, second - 2 * t);
                }
                ctx.DrawRect(new Rect(x0, first, x1 - x0, t), color);
                ctx.DrawRect(new Rect(x0, second, x1 - x0, t), color);
                break;
            }
            case TerminalUnderline.Dotted:
                Pattern(ctx, color, left, x0, x1, y, t, on: t, off: t);
                break;
            case TerminalUnderline.Dashed:
                Pattern(ctx, color, left, x0, x1, y, t, on: 3 * t, off: 2 * t);
                break;
            case TerminalUnderline.Curly:
                Curl(ctx, color, left, from, to, y, t, bottom, scale);
                break;
            default:
                ctx.DrawRect(new Rect(x0, y, x1 - x0, t), color);
                break;
        }
    }

    /// <summary>Dots or dashes laid on a grid from the row's left edge, so a run that starts
    /// mid-row continues its neighbour's pattern instead of restarting it.</summary>
    private static void Pattern(IRenderContext ctx, Color color, float left, float x0, float x1, float y, float t,
        float on, float off)
    {
        float period = on + off;
        float x = left + MathF.Floor((x0 - left) / period) * period;
        for (; x < x1; x += period)
        {
            float a = MathF.Max(x, x0), b = MathF.Min(x + on, x1);
            if (b > a) ctx.DrawRect(new Rect(a, y, b - a, t), color);
        }
    }

    /// <summary>A wave, one period a cell, drawn as short lines: there is no path to fill.</summary>
    private void Curl(IRenderContext ctx, Color color, float left, int from, int to, float y, float t, float bottom,
        float scale)
    {
        const int Steps = 8;
        float amplitude = MathF.Max(1f / scale, t);
        float centre = MathF.Min(y + amplitude, bottom - amplitude - t / 2);
        float step = _cellWidth / Steps;
        var previous = new Point(left + from * _cellWidth, centre);
        for (int i = 1; i <= (to - from) * Steps; i++)
        {
            float x = left + from * _cellWidth + i * step;
            var next = new Point(x, centre - amplitude * MathF.Sin(i * MathF.Tau / Steps));
            ctx.DrawLine(previous, next, color, t);
            previous = next;
        }
    }

    // ──── Blinking ────

    /// <summary>Starts the clock when something blinks, stops it (showing everything) when
    /// nothing does. Called after each frame is drawn.</summary>
    private void TendBlink(TerminalSnapshot s)
    {
        bool needed = _sawBlinkingText || (s.CursorBlink && s.CursorVisible && IsFocused);
        _sawBlinkingText = false;
        if (!needed)
        {
            StopBlink();
            return;
        }
        if (_blinkRunning) return;
        _blinkRunning = true;
        _blinkTimer = Animate.Value(0f, 1f, static _ => { })
            .Duration((float)BlinkInterval.TotalSeconds)
            .OnComplete(OnBlink)
            .Start();
    }

    private void OnBlink()
    {
        _blinkRunning = false;
        if (!IsHosted) return;
        _blinkOn = !_blinkOn;
        MarkPaintDirty(); // the next frame draws the new phase and starts the next half
    }

    /// <summary>Shows the cursor and blinking text now, and starts the phase over: after a key, as
    /// every terminal does, the cursor is where the eye looks.</summary>
    private void RestartBlink()
    {
        if (_blinkOn && !_blinkRunning) return;
        StopBlink();
        if (IsHosted) MarkPaintDirty();
    }

    private void StopBlink()
    {
        if (_blinkRunning)
        {
            _blinkTimer.Cancel();
            _blinkRunning = false;
        }
        _blinkOn = true;
    }
}
