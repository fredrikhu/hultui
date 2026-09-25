using HultUI;
using HultUI.Core.Styling;
using HultUI.Media;
using HultUI.Media.Mpv;
using HultUI.Samples.VideoPlayer;

// Optional argument: a file to open at once. Otherwise use Open…, or drop a file on
// the window. libmpv must be findable (HULTUI_MPV_PATH, beside the app, or the
// HultUI.Media.Mpv.Natives package); without it the window explains instead.
using var app = new Application("HultUI - Video Player", 1100, 700);
app.Theme = Theme.Dark;

Func<IMediaBackend>? backends = MpvBackend.IsAvailable ? () => new MpvBackend() : null;
// --start=<seconds> jumps there once the file is open. For checking anything that depends on
// what is actually on screen: a film's first minutes are often black, and a capture taken then
// says nothing about the picture.
double.TryParse(args.FirstOrDefault(a => a.StartsWith("--start="))?["--start=".Length..],
    System.Globalization.CultureInfo.InvariantCulture, out double startSeconds);
using var player = new VideoPlayerApp(app, app.PrimaryWindow, backends,
    args.FirstOrDefault(a => !a.StartsWith('-')), startSeconds);
app.SetRoot(player.Mount());
app.Run();
