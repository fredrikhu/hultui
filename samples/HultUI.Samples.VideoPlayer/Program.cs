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
using var player = new VideoPlayerApp(app, app.PrimaryWindow, backends, args.Length > 0 ? args[0] : null);
app.SetRoot(player.Mount());
app.Run();
