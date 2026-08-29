#:project ../../../src/HultUI.Headless/HultUI.Headless.csproj
#:project ../../../apps/Junction/Junction.csproj
#:project ../../../apps/VesperCommander/VesperCommander.csproj
#:project ../../../apps/Meridian/Meridian/Meridian.csproj

// Drives a HultUI app off-screen and writes PNGs of what it drew.
//
// The apps put an SDL window on the user's desktop, and there is no safe way to
// drive that: SetForegroundWindow from a background shell routinely fails to raise
// it, so synthesized input lands in whatever the user is actually doing, and a
// screen grab at the window's rectangle returns whatever is sitting on top of it —
// their screen, not the app. (Ask how I know.) HeadlessSession runs the same
// element tree, layout, input dispatch and Skia renderer against a CPU surface, so
// CapturePixels() is the app's own frame with nothing else in it.
//
// Usage:
//   dotnet run .claude/skills/run-hultui-app/driver.cs -- --app junction --repo . \
//       --out shots key:Digit4 waitfor:"Summary (required)" shot:changes
//
// Run it with no steps to get --out/frame.png plus the accessibility tree on stdout,
// which is how you find out what there is to click.

using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;

string app = "junction", outDir = "shots", repo = "", dir = "", dir2 = "";
int width = 1400, height = 900;
var steps = new List<string>();

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--app": app = args[++i].ToLowerInvariant(); break;
        case "--out": outDir = args[++i]; break;
        case "--repo": repo = Path.GetFullPath(args[++i]); break;
        case "--dir": dir = Path.GetFullPath(args[++i]); break;
        case "--dir2": dir2 = Path.GetFullPath(args[++i]); break;
        case "--size":
            var wh = args[++i].Split('x');
            width = int.Parse(wh[0]); height = int.Parse(wh[1]);
            break;
        default: steps.Add(args[i]); break;
    }
}

Directory.CreateDirectory(outDir);

// Point every app's config at a throwaway directory before any of them is constructed.
// They read real settings from disk on startup and write them back, so without this a
// driver run reorders the user's workspaces, or leaves their file manager showing
// whichever folder the driver happened to visit. The two apps use different env vars
// for it; Meridian persists nothing.
var scratchConfig = Path.Combine(Path.GetTempPath(), "hultui-driver-" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(scratchConfig);
Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", scratchConfig);      // Junction
Environment.SetEnvironmentVariable("VESPER_CONFIG_DIR", scratchConfig);    // Vesper Commander

using var session = HeadlessSession.Create(width, height);
IDisposable? instance = null;
try
{
    Element root = app switch
    {
        "junction" => MountJunction(),
        "vesper" or "vespercommander" => MountVesper(),
        "meridian" => MountMeridian(),
        _ => throw new ArgumentException($"unknown --app '{app}' (junction | vesper | meridian)"),
    };
    session.Application.SetRoot(root);
    session.PumpFrames(3);
    Settle(1200);

    foreach (var step in steps) Run(step);

    // No steps means "show me what it looks like and what is in it".
    if (steps.Count == 0)
    {
        Shot("frame");
        Console.WriteLine(session.Window.DumpAccessibilityTree());
    }
}
finally
{
    instance?.Dispose();
    try { Directory.Delete(scratchConfig, recursive: true); } catch (IOException) { }
}

Element MountJunction()
{
    var path = repo.Length > 0 ? repo : Junction.RepositoryPathResolver.FindGitRoot(Environment.CurrentDirectory)
        ?? throw new InvalidOperationException("--repo not given and the working directory is not in a git repo");
    var settings = new Junction.JunctionSettingsData
    {
        RepositoryPaths = [path],
        ActiveRepoPath = path,
        Workspaces = [new Junction.WorkspaceData { Id = "w1", Name = "Driver", RepoPaths = [path] }],
        ActiveWorkspaceId = "w1",
    };
    var a = new Junction.JunctionApp(session.Application, session.Window.Window, settings);
    instance = a;
    return a.Mount();
}

Element MountVesper()
{
    var left = dir.Length > 0 ? dir : Environment.CurrentDirectory;
    var right = dir2.Length > 0 ? dir2 : left;
    var a = new VesperCommander.CommanderApp(session.Application, session.Window.Window, left, right);
    instance = a as IDisposable;
    var root = a.Mount();
    // The constructor arguments are only a seed: the app restores the folders from its
    // saved session on startup, so a pane shows wherever it was last left rather than
    // what was asked for. Navigate both explicitly, which is what the Vesper tests do.
    var navigate = typeof(VesperCommander.CommanderApp).GetMethod("NavigateTo",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
    navigate.Invoke(a, ['L', left, null, false]);
    navigate.Invoke(a, ['R', right, null, false]);
    return root;
}

Element MountMeridian()
{
    var a = new Meridian.MeridianApp(session.Application, session.Window.Window);
    instance = a as IDisposable;
    return a.Mount();
}

void Run(string step)
{
    var (verb, arg) = Split(step, ':');
    switch (verb)
    {
        case "shot": Shot(arg); break;
        case "dump": Console.WriteLine(session.Window.DumpAccessibilityTree()); break;
        case "settle": Settle(arg.Length > 0 ? int.Parse(arg) : 1200); break;
        case "key": Key(arg); break;
        case "click": Click(arg); break;
        case "drag": Drag(arg); break;
        case "waitfor": WaitFor(arg); break;
        case "size": Resize(arg); break;
        default: throw new ArgumentException($"unknown step '{step}'");
    }
}

// Resizes the client area mid-run, so a frame can be compared against the same tree at
// another width. Worth having as a step rather than as a second --size run: the bugs this
// finds are the ones where a relayout uses something measured at the old width, and a fresh
// process at the new width measures everything correctly and shows nothing.
void Resize(string arg)
{
    var (w, h) = Split(arg, 'x');
    session.Window.SetClientSize(int.Parse(w), int.Parse(h));
    Settle(600);
}

void Shot(string name)
{
    using var image = session.Window.CapturePixels();
    using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
    var file = Path.Combine(outDir, (name.Length > 0 ? name : "frame") + ".png");
    using var stream = File.Create(file);
    data.SaveTo(stream);
    Console.WriteLine($"shot {file}");
}

// Frames, not sleeps: work lands on the UI thread when the session pumps, so a bare
// wait pumps nothing and the loaders never finish.
void Settle(int ms)
{
    var until = DateTime.UtcNow.AddMilliseconds(ms);
    while (DateTime.UtcNow < until) session.PumpFrames(2);
}

void Key(string arg)
{
    var parts = arg.Split('+');
    var mods = KeyModifiers.None;
    foreach (var m in parts.Skip(1))
        mods |= Enum.Parse<KeyModifiers>(m, ignoreCase: true);
    session.Window.PressKey(Enum.Parse<KeyCode>(parts[0], ignoreCase: true), mods);
    session.PumpFrames(3);
}

void Click(string arg)
{
    var (x, y) = Point(arg);
    session.Window.Click(x, y);
    session.PumpFrames(3);
}

void Drag(string arg)
{
    var bits = arg.Split(',');
    float x1, y1, x2, y2;
    if (bits[0].StartsWith('@'))
    {
        // @label,dx[,dy] — from the centre of a named element by a delta.
        (x1, y1) = Center(Find(bits[0][1..]));
        x2 = x1 + float.Parse(bits[1]);
        y2 = y1 + (bits.Length > 2 ? float.Parse(bits[2]) : 0f);
    }
    else
    {
        x1 = float.Parse(bits[0]); y1 = float.Parse(bits[1]);
        x2 = float.Parse(bits[2]); y2 = float.Parse(bits[3]);
    }
    // Press, move, release as separate events: a drag that jumps straight to the end
    // never crosses the threshold some handlers wait for.
    session.Window.MouseDown(x1, y1);
    session.Window.MoveMouse((x1 + x2) / 2f, (y1 + y2) / 2f);
    session.Window.MoveMouse(x2, y2);
    session.Window.MouseUp(x2, y2);
    session.PumpFrames(4);
}

void WaitFor(string arg)
{
    var bits = arg.Split(',');
    var text = bits[0];
    var seconds = bits.Length > 1 ? double.Parse(bits[1]) : 30;
    session.PumpUntil(() => session.Window.FindByText(text) is not null, TimeSpan.FromSeconds(seconds));
    session.PumpFrames(3);
}

(float X, float Y) Point(string arg) =>
    arg.StartsWith('@')
        ? Center(Find(arg[1..]))
        : (float.Parse(arg.Split(',')[0]), float.Parse(arg.Split(',')[1]));

HeadlessElement Find(string text) =>
    session.Window.FindByText(text)
    ?? throw new InvalidOperationException(
        $"nothing on screen reads \"{text}\" — run with no steps, or add a dump step, to see the tree");

(float X, float Y) Center(HeadlessElement e)
{
    var b = e.Element.Layout.AbsoluteBorderBox;
    return (b.X + b.Width / 2f, b.Y + b.Height / 2f);
}

static (string Verb, string Arg) Split(string s, char sep)
{
    int i = s.IndexOf(sep);
    return i < 0 ? (s, "") : (s[..i], s[(i + 1)..]);
}
