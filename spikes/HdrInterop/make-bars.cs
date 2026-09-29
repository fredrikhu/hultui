// Writes a Y4M of eight vertical bars whose RGB is defined in BT.2020 primaries and PQ-encoded,
// for telling whether wide-gamut colour survives to the screen. Y'CbCr through the BT.2020 NCL
// matrix, 8-bit limited range. Play it with
//   vf=format=colormatrix=bt.2020-ncl:primaries=bt.2020:gamma=pq
// since Y4M itself cannot say what its primaries are.
//
// Bars, left to right: 2020 red, green, blue, cyan, magenta, yellow — all out of BT.709's gamut
// — then grey and a BT.709 green re-expressed in 2020, both in gamut: the controls that must come
// out with no negative channel.
// Usage: dotnet run spikes/HdrInterop/make-bars.cs -- <out.y4m> [seconds]
// Then: dotnet run --project spikes/HdrInterop -- --range <out.y4m> "vf=format=colormatrix=bt.2020-ncl:primaries=bt.2020:gamma=pq" target-prim=bt.709 target-trc=linear target-peak=455 gamut-mapping-mode=clip depth=16 bars=8
using System.Text;

string path = args[0];
int seconds = args.Length > 1 ? int.Parse(args[1]) : 10;
const int W = 320, H = 180, Fps = 24;
const double V = 0.5;   // PQ code value of a lit channel: about 92 nits

static double Pq(double nits)
{
    const double m1 = 0.1593017578125, m2 = 78.84375, c1 = 0.8359375, c2 = 18.8515625, c3 = 18.6875;
    double y = Math.Pow(Math.Max(nits, 0) / 10000.0, m1);
    return Math.Pow((c1 + c2 * y) / (1 + c3 * y), m2);
}
static double PqToNits(double e)
{
    const double m1 = 0.1593017578125, m2 = 78.84375, c1 = 0.8359375, c2 = 18.8515625, c3 = 18.6875;
    double p = Math.Pow(e, 1 / m2);
    return 10000.0 * Math.Pow(Math.Max(p - c1, 0) / (c2 - c3 * p), 1 / m1);
}

// BT.709 green at the same luminance as a lit channel, converted into BT.2020 (linear), then
// encoded: an in-gamut colour that happens to be expressed in the wide container.
double g = PqToNits(V);
double[] g709in2020 = [0.3290 * g, 0.9195 * g, 0.0880 * g];

double[][] bars =
[
    [V, 0, 0], [0, V, 0], [0, 0, V], [0, V, V], [V, 0, V], [V, V, 0], [V, V, V],
    [Pq(g709in2020[0]), Pq(g709in2020[1]), Pq(g709in2020[2])],
];

const double Kr = 0.2627, Kb = 0.0593, Kg = 1 - Kr - Kb;
var yPlane = new byte[W * H];
var uPlane = new byte[W / 2 * (H / 2)];
var vPlane = new byte[W / 2 * (H / 2)];
for (int x = 0; x < W; x++)
{
    var rgb = bars[x * bars.Length / W];
    double yy = Kr * rgb[0] + Kg * rgb[1] + Kb * rgb[2];
    double cb = (rgb[2] - yy) / (2 * (1 - Kb));
    double cr = (rgb[0] - yy) / (2 * (1 - Kr));
    byte Y = (byte)Math.Clamp(Math.Round(16 + 219 * yy), 0, 255);
    byte U = (byte)Math.Clamp(Math.Round(128 + 224 * cb), 0, 255);
    byte Vv = (byte)Math.Clamp(Math.Round(128 + 224 * cr), 0, 255);
    for (int y = 0; y < H; y++) yPlane[y * W + x] = Y;
    if (x % 2 == 0) for (int y = 0; y < H / 2; y++) { uPlane[y * (W / 2) + x / 2] = U; vPlane[y * (W / 2) + x / 2] = Vv; }
}

using var f = File.Create(path);
f.Write(Encoding.ASCII.GetBytes($"YUV4MPEG2 W{W} H{H} F{Fps}:1 Ip A1:1 C420jpeg\n"));
for (int i = 0; i < seconds * Fps; i++)
{
    f.Write(Encoding.ASCII.GetBytes("FRAME\n"));
    f.Write(yPlane); f.Write(uPlane); f.Write(vPlane);
}
Console.WriteLine($"{path}: {bars.Length} bars, {seconds * Fps} frames; lit channel {g:0.0} nits");
