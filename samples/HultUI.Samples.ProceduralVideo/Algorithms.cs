namespace HultUI.Samples.ProceduralVideo;

/// <summary>
/// Makes one frame from a moment in time. Pure: the same time gives the same frame,
/// which is what lets the backend seek anywhere and play at any speed by asking for
/// different moments. Pixels are RGBx, four bytes each, rows <c>stride</c> bytes apart.
/// </summary>
public interface IFrameAlgorithm
{
    string Name { get; }
    void Render(Span<byte> pixels, int width, int height, int stride, double seconds);
}

/// <summary>Classic plasma: sums of sines over space and time, mapped through a palette.</summary>
public sealed class Plasma : IFrameAlgorithm
{
    private static readonly float[] Sin = Enumerable.Range(0, 1024).Select(i => MathF.Sin(i * MathF.Tau / 1024)).ToArray();
    private static float S(float x) => Sin[(int)(x * (1024 / MathF.Tau)) & 1023];

    public string Name => "Plasma";

    public void Render(Span<byte> pixels, int width, int height, int stride, double seconds)
    {
        float t = (float)seconds;
        float cx = width * 0.5f + width * 0.35f * MathF.Sin(t * 0.7f);
        float cy = height * 0.5f + height * 0.35f * MathF.Cos(t * 0.9f);
        for (int y = 0; y < height; y++)
        {
            var row = pixels.Slice(y * stride, width * 4);
            float fy = y * 0.045f;
            for (int x = 0; x < width; x++)
            {
                float fx = x * 0.045f;
                float dx = x - cx, dy = y - cy;
                float v = S(fx + t) + S(fy * 1.3f - t * 0.8f) + S((fx + fy) * 0.7f + t * 0.5f)
                        + S(MathF.Sqrt(dx * dx + dy * dy) * 0.06f - t * 1.5f);
                // v in [-4, 4]; three phase-shifted channels.
                int i = x * 4;
                row[i] = (byte)(127.5f + 127.5f * S(v * 0.8f));
                row[i + 1] = (byte)(127.5f + 127.5f * S(v * 0.8f + 2.1f));
                row[i + 2] = (byte)(127.5f + 127.5f * S(v * 0.8f + 4.2f));
                row[i + 3] = 255;
            }
        }
    }
}

/// <summary>A Julia set whose constant orbits the Mandelbrot cardioid with time.</summary>
public sealed class JuliaSet : IFrameAlgorithm
{
    private const int MaxIterations = 48;

    public string Name => "Julia set";

    public void Render(Span<byte> pixels, int width, int height, int stride, double seconds)
    {
        // c walks around the edge of the main cardioid, where the sets are richest.
        double a = seconds * 0.35;
        double cr = 0.7885 * Math.Cos(a) * 0.5 - 0.2, ci = 0.7885 * Math.Sin(a) * 0.6;
        double scale = 3.0 / Math.Min(width, height);
        for (int y = 0; y < height; y++)
        {
            var row = pixels.Slice(y * stride, width * 4);
            double zi0 = (y - height * 0.5) * scale;
            for (int x = 0; x < width; x++)
            {
                double zr = (x - width * 0.5) * scale, zi = zi0;
                int n = 0;
                while (n < MaxIterations && zr * zr + zi * zi < 4.0)
                {
                    double t = zr * zr - zi * zi + cr;
                    zi = 2 * zr * zi + ci;
                    zr = t;
                    n++;
                }
                int i = x * 4;
                if (n == MaxIterations) { row[i] = 8; row[i + 1] = 8; row[i + 2] = 20; }
                else
                {
                    float h = n / (float)MaxIterations;
                    row[i] = (byte)(255 * MathF.Pow(h, 0.5f));
                    row[i + 1] = (byte)(255 * MathF.Pow(h, 1.5f));
                    row[i + 2] = (byte)(120 + 135 * MathF.Sin(h * 6f));
                }
                row[i + 3] = 255;
            }
        }
    }
}

/// <summary>
/// Conway's Life at ten generations a second, from a fixed seed, drawn four pixels a
/// cell. A generation is a function of the seed and the count, so seeking back means
/// replaying from the start — cheap at this size.
/// </summary>
public sealed class GameOfLife : IFrameAlgorithm
{
    private const int Cell = 4;
    private const double GenerationsPerSecond = 10;
    private int _cols, _rows;
    private byte[] _cells = [];
    private byte[] _next = [];
    private long _generation = -1;

    public string Name => "Game of Life";

    public void Render(Span<byte> pixels, int width, int height, int stride, double seconds)
    {
        int cols = width / Cell, rows = height / Cell;
        long wanted = (long)(seconds * GenerationsPerSecond);
        if (cols != _cols || rows != _rows || wanted < _generation) Reset(cols, rows);
        while (_generation < wanted) Step();

        for (int y = 0; y < height; y++)
        {
            var row = pixels.Slice(y * stride, width * 4);
            int cy = Math.Min(y / Cell, rows - 1);
            for (int x = 0; x < width; x++)
            {
                bool alive = _cells[cy * cols + Math.Min(x / Cell, cols - 1)] != 0;
                int i = x * 4;
                row[i] = alive ? (byte)233 : (byte)18;
                row[i + 1] = alive ? (byte)196 : (byte)22;
                row[i + 2] = alive ? (byte)64 : (byte)36;
                row[i + 3] = 255;
            }
        }
    }

    private void Reset(int cols, int rows)
    {
        _cols = cols; _rows = rows;
        _cells = new byte[cols * rows];
        _next = new byte[cols * rows];
        var random = new Random(20260919);
        for (int i = 0; i < _cells.Length; i++) _cells[i] = random.Next(100) < 32 ? (byte)1 : (byte)0;
        _generation = 0;
    }

    private void Step()
    {
        int cols = _cols, rows = _rows;
        for (int y = 0; y < rows; y++)
        {
            int up = (y + rows - 1) % rows * cols, mid = y * cols, down = (y + 1) % rows * cols;
            for (int x = 0; x < cols; x++)
            {
                int l = (x + cols - 1) % cols, r = (x + 1) % cols;
                int n = _cells[up + l] + _cells[up + x] + _cells[up + r] + _cells[mid + l] + _cells[mid + r]
                      + _cells[down + l] + _cells[down + x] + _cells[down + r];
                _next[mid + x] = (byte)(n == 3 || (n == 2 && _cells[mid + x] != 0) ? 1 : 0);
            }
        }
        (_cells, _next) = (_next, _cells);
        _generation++;
    }
}
