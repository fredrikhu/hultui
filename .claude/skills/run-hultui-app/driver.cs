#:project ../../../src/HultUI.Headless/HultUI.Headless.csproj
#:project ../../../apps/Junction/Junction.csproj
#:project ../../../apps/VesperCommander/VesperCommander.csproj
#:project ../../../apps/Meridian/Meridian/Meridian.csproj
#:project ../../../apps/Aperture/Aperture/Aperture.csproj
#:project ../../../apps/Reel/Reel.csproj
#:project ../../../samples/HultUI.Samples.Fluent/HultUI.Samples.Fluent.csproj
#:project ../../../samples/HultUI.Samples.VideoPlayer/HultUI.Samples.VideoPlayer.csproj
#:project ../../../samples/HultUI.Samples.ProceduralVideo/HultUI.Samples.ProceduralVideo.csproj

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
float scale = 1f;
bool fluentDark = true;
int fluentAccent = 0;
string? mediaPath = null;
string jellyfinServer = "", jellyfinKeyFile = "", jellyfinUser = "";
string seerrServer = "", seerrKeyFile = "";
bool seerrDemo = false;
bool seerrReporter = false;
bool gitlab = false;
string theme = "";
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
        case "--dark": fluentDark = bool.Parse(args[++i]); break;
        case "--accent": fluentAccent = int.Parse(args[++i]); break;
        case "--media": mediaPath = args[++i]; break;
        // Aperture: which Jellyfin to browse, and the API key to browse it with. Without
        // them it mounts a fixture library instead, so the shell still renders offline.
        case "--server": jellyfinServer = args[++i]; break;
        case "--api-key-file": jellyfinKeyFile = args[++i]; break;
        case "--jellyfin-user": jellyfinUser = args[++i]; break;
        // Aperture's request server: a real Jellyseerr/Seerr by API key (acting as the
        // Jellyfin user when it knows them), or --seerr-demo for a fixture one.
        case "--seerr": seerrServer = args[++i]; break;
        case "--seerr-api-key-file": seerrKeyFile = args[++i]; break;
        case "--seerr-demo": seerrDemo = true; break;
        // The same fixture, signed in as someone who files problem reports but may not
        // read everyone's — "Your reports" instead of "Issues".
        case "--seerr-reporter": seerrDemo = true; seerrReporter = true; break;
        case "--size":
            var wh = args[++i].Split('x');
            width = int.Parse(wh[0]); height = int.Parse(wh[1]);
            break;
        // Windows runs most machines at 125% or 150%, and a frame captured at 1.0 cannot show
        // what a hairline does on the pixel grid there — which is where borders go soft.
        case "--scale": scale = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
        case "--gitlab": gitlab = true; break;
        case "--theme": theme = args[++i]; break;
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

using var session = HeadlessSession.Create(width, height, scale);
IDisposable? instance = null;
try
{
    Element root = app switch
    {
        "junction" => MountJunction(),
        "vesper" or "vespercommander" => MountVesper(),
        "meridian" => MountMeridian(),
        "aperture" => MountAperture(),
        "reel" => MountReel(),
        "fluent" => MountFluent(),
        "player" => MountPlayer(),
        "procedural" => MountProcedural(),
        _ => throw new ArgumentException($"unknown --app '{app}' (junction | vesper | meridian | aperture | reel | fluent | player | procedural)"),
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
    if (theme.Length > 0) settings.Appearance.Theme = theme;   // --theme: any ThemeCatalog id, e.g. dracula
    // --gitlab: borrow the signed-in GitLab accounts from the user's real settings file. Only
    // the identities travel; the tokens stay in the OS credential store, which this process
    // reads as the same Windows user. The scratch config is otherwise untouched.
    if (gitlab)
    {
        var real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HultGit", "settings.json");
        if (File.Exists(real))
        {
            var realSettings = Junction.JunctionSettings.LoadFrom(real);
            if (realSettings?.GitLabAccounts is { Count: > 0 } accounts) settings.GitLabAccounts = accounts;
            else Console.Error.WriteLine("--gitlab: no GitLab accounts in the real settings file");
        }
        else Console.Error.WriteLine($"--gitlab: no settings file at {real}");
    }
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
    var root = a.Mount();
    // --dir opens a collection, the same as picking a folder in the app. Without one
    // Meridian is an empty shell: no request list, no editor, nothing to drive.
    if (dir.Length > 0)
        typeof(Meridian.MeridianApp).GetMethod("OpenWorkspace",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(a, [dir]);
    return root;
}

// Aperture, the Jellyfin client. With --server (and --api-key-file) it browses a real
// server through the API-key sign-in — the one path that needs no password typed into a
// driver; without them it mounts a small fixture library, which renders every surface
// except real artwork.
Element MountAperture()
{
    Aperture.Core.IMediaLibrary library;
    if (jellyfinServer.Length > 0)
    {
        var key = jellyfinKeyFile.Length > 0
            ? File.ReadAllText(jellyfinKeyFile).Trim()
            : Environment.GetEnvironmentVariable("APERTURE_API_KEY")?.Trim() ?? "";
        var device = Aperture.Core.DeviceIdentity.New("aperture-driver");
        var probe = Aperture.Core.JellyfinConnection.DiscoverAsync(jellyfinServer, device).GetAwaiter().GetResult();
        if (!probe.Ok) throw new InvalidOperationException("--server: " + probe.Failure!.Message);
        var session2 = Aperture.Core.JellyfinConnection
            .SignInWithApiKeyAsync(probe.Value, key, jellyfinUser.Length > 0 ? jellyfinUser : null, device)
            .GetAwaiter().GetResult();
        if (!session2.Ok) throw new InvalidOperationException("--server: " + session2.Failure!.Message);
        library = session2.Value;
    }
    else
    {
        library = DemoLibrary();
    }

    var settings = new Aperture.Core.ApertureSettings(new HultUI.Core.Platform.InMemoryCredentialStore(),
        Path.Combine(scratchConfig, "aperture.json"));
    Func<HultUI.Media.IMediaBackend>? backends =
        HultUI.Media.Mpv.MpvBackend.IsAvailable ? () => new HultUI.Media.Mpv.MpvBackend() : null;

    Aperture.Core.Requests.IRequestServer? requests = null;
    if (seerrServer.Length > 0)
    {
        var key = seerrKeyFile.Length > 0
            ? File.ReadAllText(seerrKeyFile).Trim()
            : Environment.GetEnvironmentVariable("APERTURE_SEERR_API_KEY")?.Trim() ?? "";
        var found = Aperture.Core.Requests.SeerrConnection.DiscoverAsync(seerrServer).GetAwaiter().GetResult();
        if (!found.Ok) throw new InvalidOperationException("--seerr: " + found.Failure!.Message);
        var signedIn = Aperture.Core.Requests.SeerrConnection
            .SignInWithApiKeyAsync(found.Value, library.Server.Id, key, library.User.Id)
            .GetAwaiter().GetResult();
        if (!signedIn.Ok) throw new InvalidOperationException("--seerr: " + signedIn.Failure!.Message);
        requests = signedIn.Value;
    }
    else if (seerrDemo)
    {
        requests = DemoRequests(seerrReporter);
        if (seerrReporter)
        {
            // Read before the answer came: the answered report shows as news.
            var mine = requests.GetIssuesAsync(Aperture.Core.Requests.IssueFilter.All).GetAwaiter().GetResult().Value.Items;
            settings.RememberRequestServer(new Aperture.Core.RequestServerAccountData
            {
                JellyfinServerId = library.Server.Id,
                Address = requests.Server.Address,
                UserId = requests.User.Id,
                UserName = requests.User.DisplayName,
                SeenIssues = mine.Select(i => new Aperture.Core.SeenIssue
                {
                    Id = i.Id,
                    Comments = 1,
                    Resolved = i.Status == Aperture.Core.Requests.IssueStatus.Resolved,
                }).ToList(),
            }, "demo");
        }
    }

    var a = new Aperture.ApertureApp(session.Application, session.Window.Window, settings, library, backends,
        requests: requests);
    instance = a;
    return a.Mount();
}

// A request server with something on every shelf: a title the demo library has, one asked
// for, one on its way, a series part-way in, and someone else's request waiting.
Aperture.Core.Testing.FakeRequestServer DemoRequests(bool reporter = false)
{
    var R = reporter
        ? Aperture.Core.Requests.RequestPermission.Request | Aperture.Core.Requests.RequestPermission.CreateIssues
        : Aperture.Core.Requests.RequestPermission.Request | Aperture.Core.Requests.RequestPermission.ManageRequests
          | Aperture.Core.Requests.RequestPermission.ManageIssues;
    var fake = new Aperture.Core.Testing.FakeRequestServer(user: new Aperture.Core.Requests.RequestUser(1, "fredrik", R));
    var trending = Aperture.Core.Requests.DiscoverShelf.Trending;
    var films = Aperture.Core.Requests.DiscoverShelf.PopularMovies;
    var upcoming = Aperture.Core.Requests.DiscoverShelf.UpcomingMovies;
    var series = Aperture.Core.Requests.DiscoverShelf.PopularSeries;
    string[] names = ["Harbour Lights", "The Quiet Year", "Glass Orchard", "Northbound", "A Field in Winter",
                      "The Cartographer", "Low Tide", "Paper Moons", "Signal Fires", "The Night Ferry"];
    for (int i = 0; i < names.Length; i++)
    {
        var availability = i switch
        {
            1 => Aperture.Core.Requests.Availability.Pending,
            3 => Aperture.Core.Requests.Availability.Processing,
            _ => Aperture.Core.Requests.Availability.Unknown,
        };
        fake.AddMovie(1000 + i, names[i], 2019 + i % 6, availability, null, trending, i % 2 == 0 ? films : upcoming);
    }
    fake.AddMovie(990, "Stalker", 1979, Aperture.Core.Requests.Availability.Available, "m0", trending, films);
    fake.AddSeries(2000, "The Long Way Home", 2019, Aperture.Core.Requests.Availability.PartiallyAvailable, "s1",
    [
        new Aperture.Core.Requests.DiscoverSeason(1, "Season 1", 8, 2019, Aperture.Core.Requests.Availability.Available, Aperture.Core.Requests.Availability.Unknown),
        new Aperture.Core.Requests.DiscoverSeason(2, "Season 2", 8, 2021, Aperture.Core.Requests.Availability.Unknown, Aperture.Core.Requests.Availability.Unknown),
        new Aperture.Core.Requests.DiscoverSeason(3, "Season 3", 10, 2023, Aperture.Core.Requests.Availability.Unknown, Aperture.Core.Requests.Availability.Unknown),
    ], series, trending);
    for (int i = 0; i < 6; i++) fake.AddSeries(2100 + i, "Series " + (char)('A' + i), 2015 + i, shelves: [series]);
    fake.AddPendingRequest("movie:1004");
    fake.AddPendingRequest("tv:2102", [1, 2]);
    fake.AddRequest("movie:1003", Aperture.Core.Requests.RequestStatus.Approved);
    // A watchlist, for Home's row — one of them already in the library, which comes first.
    fake.AddToWatchlist("movie:1006");
    fake.AddToWatchlist("movie:990");
    fake.AddToWatchlist("tv:2101");
    // Something downloading, for the progress bar and the list's percentage.
    fake.SetDownloads("movie:1003", [new Aperture.Core.Requests.DownloadProgress("Northbound.2022.2160p.mkv",
        8_000_000_000, 2_600_000_000, "downloading", TimeSpan.FromMinutes(23), null)]);
    // Problem reports, for the Issues page: two open (one answered), one resolved.
    var now = DateTimeOffset.UtcNow;
    var subs = fake.AddIssue("tv:2000", Aperture.Core.Requests.IssueType.Subtitles,
        "The subtitles on this one are for the previous episode — the lines run about a scene behind.",
        1, 4, at: now.AddHours(-5));
    fake.AddComment(subs.Id, "Found it: the file was named for E03. Swapping it tonight.", 1, "fredrik");
    fake.AddComment(subs.Id, "Thanks! I'll check tomorrow.");
    fake.AddIssue("movie:990", Aperture.Core.Requests.IssueType.Audio,
        "No sound after the first reel.", at: now.AddDays(-2), by: "sam", byId: 3);
    fake.AddIssue("movie:1003", Aperture.Core.Requests.IssueType.Video, "Blocky in the dark scenes.",
        status: Aperture.Core.Requests.IssueStatus.Resolved, at: now.AddDays(-9));
    // Two of fredrik's own, for the reporter's view: one answered, one resolved.
    var mine = fake.AddIssue("movie:1000", Aperture.Core.Requests.IssueType.Audio,
        "The sound drops out for a few seconds around the hour mark.", at: now.AddDays(-1), byId: 1, by: "fredrik");
    fake.AddComment(mine.Id, "Re-downloading it — should be back in an hour.");
    fake.AddIssue("movie:1002", Aperture.Core.Requests.IssueType.Subtitles, "No English subtitles.",
        status: Aperture.Core.Requests.IssueStatus.Resolved, at: now.AddDays(-12), byId: 1, by: "fredrik");
    return fake;
}

// Enough of a library to render every surface: a shelf, something half-watched, a series
// with episodes under it.
Aperture.Core.Testing.FakeMediaLibrary DemoLibrary()
{
    var fake = new Aperture.Core.Testing.FakeMediaLibrary()
        .AddLibrary("movies", "Films", Aperture.Core.LibraryKind.Movies)
        .AddLibrary("shows", "Series", Aperture.Core.LibraryKind.TvShows);
    string[] titles = ["Stalker", "Solaris", "The Mirror", "Andrei Rublev", "Nostalghia", "The Sacrifice",
                       "Ivan's Childhood", "Mirror of Days", "Northern Light", "The Long Take"];
    for (int i = 0; i < titles.Length; i++)
        fake.AddMovie("m" + i, titles[i], "movies", 1962 + i * 3, 96 + i * 7,
            resume: i == 1 ? TimeSpan.FromMinutes(58) : null, played: i == 2);
    fake.AddItem(new Aperture.Core.MediaItem
    {
        Id = "s1", Name = "The Long Way Home", Kind = Aperture.Core.MediaKind.Series, TmdbId = 2000,
        ProductionYear = 2019, ChildCount = 8, UnplayedItemCount = 5,
        Overview = "A fixture series, for looking at the shelf.",
    }, "shows");
    for (int e = 1; e <= 8; e++)
        fake.AddItem(new Aperture.Core.MediaItem
        {
            Id = "e" + e, Name = "Episode " + e, Kind = Aperture.Core.MediaKind.Episode,
            SeriesId = "s1", SeriesName = "The Long Way Home", ParentIndexNumber = 1, IndexNumber = e,
            RunTime = TimeSpan.FromMinutes(42), Played = e <= 3,
            ResumePosition = e == 4 ? TimeSpan.FromMinutes(12) : null,
            Overview = "What happens in episode " + e + ".",
        }, "s1");
    fake.ResumeIds.Add("m1");
    fake.ResumeIds.Add("e4");
    fake.NextUpIds.Add("e5");
    return fake;
}

// The Fluent sample. `--dark false` opens it in light mode, and `--accent <0-5>` picks one of
// the Windows accent presets, so both palettes can be captured without touching the source.
Element MountFluent()
{
    var a = new HultUI.Samples.Fluent.FluentApp(session.Application, session.Window.Window);
    if (!fluentDark) a.SetDark(false);
    if (fluentAccent > 0) a.SetAccent(fluentAccent);
    return a.Mount();
}

Element MountPlayer()
{
    // --media <file> opens it through libmpv (HULTUI_MPV_PATH must point at one); with
    // no libmpv the window shows its notice. Frames arrive from a worker thread in
    // real time, so settle before a shot.
    session.Application.Theme = HultUI.Core.Styling.Theme.Dark;
    Func<HultUI.Media.IMediaBackend>? backends = HultUI.Media.Mpv.MpvBackend.IsAvailable
        ? () => new HultUI.Media.Mpv.MpvBackend() : null;
    var a = new HultUI.Samples.VideoPlayer.VideoPlayerApp(session.Application, session.Window.Window, backends,
        mediaPath is null ? null : Path.GetFullPath(mediaPath));
    instance = a;
    return a.Mount();
}

Element MountReel()
{
    // The local player. --media <file> opens one at once; without it the window shows its
    // invitation. libmpv must be findable (HULTUI_MPV_PATH), or it says so instead.
    session.Application.Theme = HultUI.Core.Styling.Theme.Dark;
    Func<HultUI.Media.IMediaBackend>? backends = HultUI.Media.Mpv.MpvBackend.IsAvailable
        ? () => new HultUI.Media.Mpv.MpvBackend() : null;
    var settings = new Reel.ReelSettings(Path.Combine(scratchConfig, "reel.json"));
    var a = new Reel.ReelApp(session.Application, session.Window.Window, settings, backends);
    instance = a;
    var root = a.Mount();
    if (mediaPath is not null) a.Open(Path.GetFullPath(mediaPath));
    return root;
}

Element MountProcedural()
{
    session.Application.Theme = HultUI.Core.Styling.Theme.Dark;
    var a = new HultUI.Samples.ProceduralVideo.ProceduralVideoApp(session.Application, session.Window.Window);
    instance = a;
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
        // Exactly n frames of virtual time (16.7 ms each). settle races the clock — it pumps
        // as fast as the CPU allows for a wall-clock span, so a 1.7 s toast is gone inside a
        // 250 ms settle; anything timed needs frames.
        case "frames": session.PumpFrames(arg.Length > 0 ? int.Parse(arg) : 1); break;
        case "key": Key(arg); break;
        case "type": Type(arg); break;
        case "click": Click(arg); break;
        case "drag": Drag(arg); break;
        case "autoscroll": AutoScroll(arg); break;
        case "wheel": Wheel(arg); break;
        case "hover": Hover(arg); break;
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

// Types into whatever has the keyboard, the way a real keyboard does: text input events,
// not synthesized key presses. Anything with a search box needs this — the filter runs off
// OnTextChanged, which a KeyDown never raises.
void Type(string text)
{
    session.Window.TypeText(text);
    session.PumpFrames(3);
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

// Holds the middle button away from where it was pressed and lets frames run, which is
// the only way to see a Windows-style autoscroll: the pointer's distance from the anchor
// is a *speed*, so the scrolling happens between input events rather than during them.
// `drag` cannot show it — press, move and release with no frames in between leaves the
// view exactly where it was, which reads as "the gesture does nothing".
void AutoScroll(string arg)
{
    var bits = arg.Split(',');
    float x, y;
    int next;
    if (bits[0].StartsWith('@')) { (x, y) = Center(Find(bits[0][1..])); next = 1; }
    else { x = float.Parse(bits[0]); y = float.Parse(bits[1]); next = 2; }
    float dy = float.Parse(bits[next]);
    int ms = bits.Length > next + 1 ? int.Parse(bits[next + 1]) : 600;

    session.Window.MouseDown(x, y, MouseButton.Middle);
    session.Window.MoveMouse(x, y + dy);
    Settle(ms);
    session.Window.MouseUp(x, y + dy, MouseButton.Middle);
    session.PumpFrames(2);
}

// Turns the wheel at a point, so a step can reach something below the fold. Notches rather
// than pixels: one notch is what a real wheel click sends, and a view that scales the delta
// itself (most of them) is only exercised properly by the value it actually receives.
void Wheel(string arg)
{
    var bits = arg.Split(',');
    float x = float.Parse(bits[0]), y = float.Parse(bits[1]), notches = float.Parse(bits[2]);
    session.Window.Wheel(x, y, 0f, notches);
    session.PumpFrames(4);
}

// Parks the pointer somewhere and lets frames run, for anything that only appears under it —
// a hover fill, a tooltip, a scrollbar that expands. `click` moves the pointer too, but it also
// presses, which is usually not what you want to photograph.
void Hover(string arg)
{
    var bits = arg.Split(',');
    float x, y;
    int next;
    if (bits[0].StartsWith('@')) { (x, y) = Center(Find(bits[0][1..])); next = 1; }
    else { x = float.Parse(bits[0]); y = float.Parse(bits[1]); next = 2; }
    session.Window.MoveMouse(x, y);
    Settle(bits.Length > next ? int.Parse(bits[next]) : 400);
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
        ? PointOf(arg[1..])
        : (float.Parse(arg.Split(',')[0]), float.Parse(arg.Split(',')[1]));

// An element if there is one, otherwise the accessibility node's own rectangle. The second
// case is a *virtual* item — a context-menu row, an unrealized row of a virtual list —
// which has bounds but no element to resolve, so FindByText comes back empty while the
// thing is plainly on screen. Those rows are clickable by position like anything else.
(float X, float Y) PointOf(string text)
{
    if (session.Window.FindByText(text) is { } element) return Center(element);

    foreach (var node in Walk(session.Window.AccessibilityTree))
    {
        if (node.Name != text && node.Value != text) continue;
        var r = node.BoundingRect;
        if (r.Width <= 0 || r.Height <= 0) continue;
        return (r.X + r.Width / 2f, r.Y + r.Height / 2f);
    }
    throw new InvalidOperationException(
        $"nothing on screen reads \"{text}\" — run with no steps, or add a dump step, to see the tree");
}

static IEnumerable<HultUI.Core.Accessibility.AccessibilityNode> Walk(
    HultUI.Core.Accessibility.AccessibilityNode node)
{
    yield return node;
    foreach (var child in node.Children)
        foreach (var descendant in Walk(child))
            yield return descendant;
}

HeadlessElement Find(string text) =>
    session.Window.FindByText(text)
    ?? throw new InvalidOperationException(
        $"nothing on screen reads \"{text}\" — run with no steps, or add a dump step, to see the tree");

// ScreenBorderBox, not the layout box: inside a scrolled view the two differ by the scroll
// amount, and a `click:@Something` aimed at the layout box lands on whatever else is there and
// silently does nothing. The steps here need a point rather than a click, so they cannot just
// call session.Window.Click(element).
(float X, float Y) Center(HeadlessElement e)
{
    var b = e.Element.ScreenBorderBox;
    return (b.X + b.Width / 2f, b.Y + b.Height / 2f);
}

static (string Verb, string Arg) Split(string s, char sep)
{
    int i = s.IndexOf(sep);
    return i < 0 ? (s, "") : (s[..i], s[(i + 1)..]);
}
