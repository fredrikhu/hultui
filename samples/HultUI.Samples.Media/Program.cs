using HultUI;
using HultUI.Core.Styling;
using HultUI.Media;
using HultUI.Media.Mpv;
using HultUI.Samples.Media;

// With a file or URL argument the sample plays it through libmpv (which must be
// findable — see src/HultUI.Media.Mpv/README.md); without one it plays the
// synthetic test pattern and needs no native library at all.
IMediaBackend backend;
MediaSource source;
if (args.Length > 0)
{
    if (!MpvBackend.IsAvailable)
    {
        Console.Error.WriteLine("libmpv was not found; put libmpv-2.dll beside the sample or set HULTUI_MPV_PATH.");
        return 1;
    }
    backend = new MpvBackend();
    source = Uri.TryCreate(args[0], UriKind.Absolute, out var uri) && !uri.IsFile
        ? MediaSource.FromUri(uri)
        : MediaSource.FromFile(Path.GetFullPath(args[0]));
}
else
{
    backend = new SyntheticMediaBackend();
    source = MediaSource.FromFile("test-pattern.synthetic");
}

using var app = new Application("HultUI - Video", 960, 620);
app.Theme = Theme.Dark;

using var sample = new MediaSampleApp(app, app.PrimaryWindow, backend, source);
app.SetRoot(sample.Mount());
app.Run();
return 0;
