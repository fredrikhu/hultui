using HultUI.Core.Primitives;
using HultUI.Core.Rendering;

namespace HultUI.Terminal.Rendering;

/// <summary>
/// Box drawing (U+2500–257F), block elements (U+2580–259F) and the Powerline separators
/// (U+E0B0–E0BF) drawn as geometry rather than from the font, so lines meet across cells whatever
/// the font's line height, and a prompt's separators show in a face that has none. Windows
/// Terminal, kitty and Ghostty do the same. Dashed and diagonal lines are left to the font.
/// </summary>
internal static class BoxDrawing
{
    // Arm weights per character, in the order up, right, down, left:
    // 0 none, 1 light, 2 heavy, 3 double. The rounded corners (U+256D–2570) are drawn by Arc.
    private static readonly Dictionary<int, string> Arms = new()
    {
        [0x2500] = "0101", [0x2501] = "0202", [0x2502] = "1010", [0x2503] = "2020",
        [0x250C] = "0110", [0x250D] = "0210", [0x250E] = "0120", [0x250F] = "0220",
        [0x2510] = "0011", [0x2511] = "0012", [0x2512] = "0021", [0x2513] = "0022",
        [0x2514] = "1100", [0x2515] = "1200", [0x2516] = "2100", [0x2517] = "2200",
        [0x2518] = "1001", [0x2519] = "1002", [0x251A] = "2001", [0x251B] = "2002",
        [0x251C] = "1110", [0x251D] = "1210", [0x251E] = "2110", [0x251F] = "1120",
        [0x2520] = "2120", [0x2521] = "2210", [0x2522] = "1220", [0x2523] = "2220",
        [0x2524] = "1011", [0x2525] = "1012", [0x2526] = "2011", [0x2527] = "1021",
        [0x2528] = "2021", [0x2529] = "2012", [0x252A] = "1022", [0x252B] = "2022",
        [0x252C] = "0111", [0x252D] = "0112", [0x252E] = "0211", [0x252F] = "0212",
        [0x2530] = "0121", [0x2531] = "0122", [0x2532] = "0221", [0x2533] = "0222",
        [0x2534] = "1101", [0x2535] = "1102", [0x2536] = "1201", [0x2537] = "1202",
        [0x2538] = "2101", [0x2539] = "2102", [0x253A] = "2201", [0x253B] = "2202",
        [0x253C] = "1111", [0x253D] = "1112", [0x253E] = "1211", [0x253F] = "1212",
        [0x2540] = "2111", [0x2541] = "1121", [0x2542] = "2121", [0x2543] = "2112",
        [0x2544] = "2211", [0x2545] = "1122", [0x2546] = "1221", [0x2547] = "2212",
        [0x2548] = "1222", [0x2549] = "2122", [0x254A] = "2221", [0x254B] = "2222",
        [0x2550] = "0303", [0x2551] = "3030",
        [0x2552] = "0310", [0x2553] = "0130", [0x2554] = "0330",
        [0x2555] = "0013", [0x2556] = "0031", [0x2557] = "0033",
        [0x2558] = "1300", [0x2559] = "3100", [0x255A] = "3300",
        [0x255B] = "1003", [0x255C] = "3001", [0x255D] = "3003",
        [0x255E] = "1310", [0x255F] = "3130", [0x2560] = "3330",
        [0x2561] = "1013", [0x2562] = "3031", [0x2563] = "3033",
        [0x2564] = "0313", [0x2565] = "0131", [0x2566] = "0333",
        [0x2567] = "1303", [0x2568] = "3101", [0x2569] = "3303",
        [0x256A] = "1313", [0x256B] = "3131", [0x256C] = "3333",
        [0x256D] = "0110", [0x256E] = "0011", [0x256F] = "1001", [0x2570] = "1100",
        [0x2574] = "0001", [0x2575] = "1000", [0x2576] = "0100", [0x2577] = "0010",
        [0x2578] = "0002", [0x2579] = "2000", [0x257A] = "0200", [0x257B] = "0020",
        [0x257C] = "0201", [0x257D] = "1020", [0x257E] = "0102", [0x257F] = "2010",
    };

    /// <summary>Whether <paramref name="codePoint"/> is drawn here rather than from the font.</summary>
    public static bool Handles(int codePoint) =>
        Arms.ContainsKey(codePoint) || codePoint is >= 0x2580 and <= 0x259F or >= 0xE0B0 and <= 0xE0BF;

    /// <summary>Draws <paramref name="codePoint"/> into <paramref name="cell"/>. Returns false for a
    /// character this does not draw.</summary>
    public static bool TryDraw(IRenderContext ctx, int codePoint, Rect cell, Color color, float scale)
    {
        if (codePoint is >= 0x256D and <= 0x2570)
        {
            Arc(ctx, codePoint, cell, color, scale);
            return true;
        }
        if (codePoint is >= 0xE0B0 and <= 0xE0BF)
        {
            Powerline(ctx, codePoint, cell, color, scale);
            return true;
        }
        if (Arms.TryGetValue(codePoint, out var arms))
        {
            DrawLines(ctx, arms, cell, color, scale);
            return true;
        }
        if (codePoint is >= 0x2580 and <= 0x259F)
        {
            DrawBlock(ctx, codePoint, cell, color);
            return true;
        }
        return false;
    }

    private static void DrawLines(IRenderContext ctx, string arms, Rect cell, Color color, float scale)
    {
        // Thicknesses in whole device pixels, so a line is crisp and every cell's line is the same.
        float px = 1f / scale;
        float light = MathF.Max(1, MathF.Round(cell.Height * scale / 16f)) * px;
        float heavy = light * 2;
        float gap = light; // between the two strokes of a double line

        // The middle of the cell, as it is: the fills that draw the lines snap their own edges, so
        // the bands land on whole pixels, and every cell in a column (or row) has the same snapped
        // bounds, so its lines line up. Snapping the middle first moved a one-pixel line off it in
        // a cell an odd number of pixels wide: 19.5 became 20, and the fill took the band from
        // 19.5..20.5 over to column 20, leaving four pixels on one side and two on the other.
        float cx = cell.X + cell.Width / 2f;
        float cy = cell.Y + cell.Height / 2f;

        int up = arms[0] - '0', right = arms[1] - '0', down = arms[2] - '0', left = arms[3] - '0';

        // How far a horizontal arm must reach past the centre to meet the vertical strokes, and the
        // other way round: half the widest perpendicular stroke.
        float halfV = HalfWidth(Math.Max(up, down), light, heavy, gap);
        float halfH = HalfWidth(Math.Max(left, right), light, heavy, gap);

        if (left != 0) Horizontal(ctx, cell.X, cx + halfV, cy, left, light, heavy, gap, color);
        if (right != 0) Horizontal(ctx, cx - halfV, cell.X + cell.Width, cy, right, light, heavy, gap, color);
        if (up != 0) Vertical(ctx, cell.Y, cy + halfH, cx, up, light, heavy, gap, color);
        if (down != 0) Vertical(ctx, cy - halfH, cell.Y + cell.Height, cx, down, light, heavy, gap, color);
    }

    /// <summary>The light line's thickness: whole device pixels, a sixteenth of the cell's height.</summary>
    private static float Light(Rect cell, float scale) => MathF.Max(1, MathF.Round(cell.Height * scale / 16f)) / scale;

    /// <summary>
    /// A rounded corner: a quarter circle from the vertical line to the cell's edge, where it meets
    /// the horizontal arm tangent to it, and runs on straight into the vertical one. Drawn as the
    /// corner of a rounded box whose sides are the bands the plain lines fill, clipped to the cell,
    /// so its straight parts are the same pixels as the lines they join.
    /// </summary>
    private static void Arc(IRenderContext ctx, int cp, Rect cell, Color color, float scale)
    {
        float light = Light(cell, scale);
        bool right = cp is 0x256D or 0x2570, down = cp is 0x256D or 0x256E; // ╭ ╮ ╯ ╰

        // The vertical line's left edge and the horizontal line's top, where DrawLines' fills land.
        float bandX = BandStart(cell.X + cell.Width / 2f, light, scale);
        float bandY = BandStart(cell.Y + cell.Height / 2f, light, scale);

        // The outer radius runs from the vertical band's far side to the cell's edge, so the curve
        // turns straight exactly where the next cell's line begins. Half the cell's width was that
        // only when the band sat in the cell's middle: in a cell an odd number of pixels wide it
        // came out half a pixel long, and the arc was still curving at the edge — a notch in every
        // join, which DejaVu Sans Mono's 7.82px cells (Linux's monospace) showed in every row.
        float across = right ? cell.X + cell.Width - bandX : bandX + light - cell.X;
        float along = down ? cell.Y + cell.Height - bandY : bandY + light - cell.Y;
        float radius = MathF.Max(light, MathF.Min(across, along));

        float big = cell.Width + cell.Height; // the box's far sides fall outside the cell
        float x = right ? bandX : bandX + light - big;
        float y = down ? bandY : bandY + light - big;
        ctx.PushClip(cell);
        // The stroke is inside the box, so its outer edge follows the box's corner.
        ctx.DrawRoundedRect(new Rect(x, y, big, big), new CornerRadius(radius), default, color, new Thickness(light));
        ctx.Pop();
    }

    /// <summary>Where a band <paramref name="thickness"/> wide centred on <paramref name="centre"/>
    /// starts once drawn: on the device grid, rounded the way a fill's edges are, so the arc's
    /// sides fall on the very pixels <see cref="Horizontal"/> and <see cref="Vertical"/> fill.</summary>
    private static float BandStart(float centre, float thickness, float scale) =>
        MathF.Round((centre - thickness / 2) * scale, MidpointRounding.AwayFromZero) / scale;

    /// <summary>
    /// The Powerline separators: solid and thin triangles, half-circles and diagonals, filling the
    /// cell edge to edge so a prompt's segments meet without a seam.
    /// </summary>
    private static void Powerline(IRenderContext ctx, int cp, Rect c, Color color, float scale)
    {
        float x0 = c.X, y0 = c.Y, x1 = c.X + c.Width, y1 = c.Y + c.Height, ym = c.Y + c.Height / 2f;
        float thin = Light(c, scale);
        switch (cp)
        {
            case 0xE0B0: ctx.FillPolygon([new(x0, y0), new(x1, ym), new(x0, y1)], color); return;
            case 0xE0B2: ctx.FillPolygon([new(x1, y0), new(x0, ym), new(x1, y1)], color); return;
            case 0xE0B4: ctx.FillPolygon(HalfEllipse(c, facingRight: true), color); return;
            case 0xE0B6: ctx.FillPolygon(HalfEllipse(c, facingRight: false), color); return;
            case 0xE0B8: ctx.FillPolygon([new(x0, y0), new(x1, y1), new(x0, y1)], color); return;
            case 0xE0BA: ctx.FillPolygon([new(x1, y0), new(x1, y1), new(x0, y1)], color); return;
            case 0xE0BC: ctx.FillPolygon([new(x0, y0), new(x1, y0), new(x0, y1)], color); return;
            case 0xE0BE: ctx.FillPolygon([new(x0, y0), new(x1, y0), new(x1, y1)], color); return;
        }
        // The thin ones, clipped so the stroke's ends stay in the cell.
        ctx.PushClip(c);
        switch (cp)
        {
            case 0xE0B1: ctx.DrawPolyline([new(x0, y0), new(x1, ym), new(x0, y1)], color, thin); break;
            case 0xE0B3: ctx.DrawPolyline([new(x1, y0), new(x0, ym), new(x1, y1)], color, thin); break;
            case 0xE0B5: ctx.DrawPolyline(HalfEllipse(c, facingRight: true, inset: thin / 2), color, thin); break;
            case 0xE0B7: ctx.DrawPolyline(HalfEllipse(c, facingRight: false, inset: thin / 2), color, thin); break;
            case 0xE0B9 or 0xE0BF: ctx.DrawPolyline([new(x0, y0), new(x1, y1)], color, thin); break;
            case 0xE0BB or 0xE0BD: ctx.DrawPolyline([new(x0, y1), new(x1, y0)], color, thin); break;
        }
        ctx.Pop();
    }

    /// <summary>The curve of a half-ellipse filling the cell, its flat side on the cell's left edge
    /// (facing right) or right edge, top to bottom; <paramref name="inset"/> pulls it in for a
    /// stroke.</summary>
    private static Point[] HalfEllipse(Rect c, bool facingRight, float inset = 0)
    {
        const int Segments = 24;
        float flat = facingRight ? c.X : c.X + c.Width;
        float rx = c.Width - inset, ry = c.Height / 2f - inset, cy = c.Y + c.Height / 2f;
        var points = new Point[Segments + 1];
        for (int i = 0; i <= Segments; i++)
        {
            double t = -Math.PI / 2 + Math.PI * i / Segments;
            float dx = (float)Math.Cos(t) * rx;
            points[i] = new Point(facingRight ? flat + dx : flat - dx, cy + (float)Math.Sin(t) * ry);
        }
        return points;
    }

    private static float HalfWidth(int weight, float light, float heavy, float gap) => weight switch
    {
        1 => light / 2,
        2 => heavy / 2,
        3 => light + gap / 2,
        _ => 0,
    };

    private static void Horizontal(IRenderContext ctx, float x0, float x1, float cy, int weight,
        float light, float heavy, float gap, Color color)
    {
        if (x1 <= x0) return;
        if (weight == 3)
        {
            ctx.DrawRect(new Rect(x0, cy - gap / 2 - light, x1 - x0, light), color);
            ctx.DrawRect(new Rect(x0, cy + gap / 2, x1 - x0, light), color);
            return;
        }
        float t = weight == 2 ? heavy : light;
        ctx.DrawRect(new Rect(x0, cy - t / 2, x1 - x0, t), color);
    }

    private static void Vertical(IRenderContext ctx, float y0, float y1, float cx, int weight,
        float light, float heavy, float gap, Color color)
    {
        if (y1 <= y0) return;
        if (weight == 3)
        {
            ctx.DrawRect(new Rect(cx - gap / 2 - light, y0, light, y1 - y0), color);
            ctx.DrawRect(new Rect(cx + gap / 2, y0, light, y1 - y0), color);
            return;
        }
        float t = weight == 2 ? heavy : light;
        ctx.DrawRect(new Rect(cx - t / 2, y0, t, y1 - y0), color);
    }

    private static void DrawBlock(IRenderContext ctx, int cp, Rect c, Color color)
    {
        float w = c.Width, h = c.Height;
        switch (cp)
        {
            case 0x2580: ctx.DrawRect(new Rect(c.X, c.Y, w, h / 2), color); return;           // ▀
            case >= 0x2581 and <= 0x2588:                                                       // ▁ … █
                float eighths = cp - 0x2580;
                ctx.DrawRect(new Rect(c.X, c.Y + h * (1 - eighths / 8), w, h * eighths / 8), color);
                return;
            case >= 0x2589 and <= 0x258F:                                                       // ▉ … ▏
                float left = 0x2590 - cp;
                ctx.DrawRect(new Rect(c.X, c.Y, w * left / 8, h), color);
                return;
            case 0x2590: ctx.DrawRect(new Rect(c.X + w / 2, c.Y, w / 2, h), color); return;     // ▐
            case 0x2591: ctx.DrawRect(c, color with { A = (byte)(color.A / 4) }); return;       // ░
            case 0x2592: ctx.DrawRect(c, color with { A = (byte)(color.A / 2) }); return;       // ▒
            case 0x2593: ctx.DrawRect(c, color with { A = (byte)(color.A * 3 / 4) }); return;   // ▓
            case 0x2594: ctx.DrawRect(new Rect(c.X, c.Y, w, h / 8), color); return;             // ▔
            case 0x2595: ctx.DrawRect(new Rect(c.X + w * 7 / 8, c.Y, w / 8, h), color); return; // ▕
        }

        // Quadrants: upper-left, upper-right, lower-left, lower-right.
        int quads = cp switch
        {
            0x2596 => 0b0010, 0x2597 => 0b0001, 0x2598 => 0b1000, 0x2599 => 0b1011,
            0x259A => 0b1001, 0x259B => 0b1110, 0x259C => 0b1101, 0x259D => 0b0100,
            0x259E => 0b0110, 0x259F => 0b0111, _ => 0,
        };
        if ((quads & 0b1000) != 0) ctx.DrawRect(new Rect(c.X, c.Y, w / 2, h / 2), color);
        if ((quads & 0b0100) != 0) ctx.DrawRect(new Rect(c.X + w / 2, c.Y, w / 2, h / 2), color);
        if ((quads & 0b0010) != 0) ctx.DrawRect(new Rect(c.X, c.Y + h / 2, w / 2, h / 2), color);
        if ((quads & 0b0001) != 0) ctx.DrawRect(new Rect(c.X + w / 2, c.Y + h / 2, w / 2, h / 2), color);
    }
}
