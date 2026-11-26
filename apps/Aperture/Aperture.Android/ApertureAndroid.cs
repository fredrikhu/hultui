using Aperture.Core;
using Aperture.Core.Testing;
using Aperture.Theme;
using HultUI;
using HultUI.Core.Platform;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Media;
using HultUI.Media.Mpv;
using Application = HultUI.Application;

namespace Aperture.Android;

/// <summary>
/// Aperture on Android: what the desktop's Program.cs does, but the window. The settings and the
/// token go where the platform keeps them (CredentialStore.Default is the Android Keystore there);
/// a Vulkan request draws with GL ES on Android (RendererSelection).
/// </summary>
internal static class ApertureAndroid
{
    /// <summary>Debug builds: browse a made-up library, with no server (<c>-e demo 1</c>).</summary>
    public static bool Demo;

    public static void Run()
    {
        using var app = new Application("Aperture", 412, 915, WindowStyle.Borderless,
            renderer: RendererKind.Vulkan);
        app.Theme = Halide.ToHultTheme();
        // A phone's card sizes, on a screen whose shorter side is under 600 DIP; a tablet keeps the
        // desktop's. Before anything is built: the sizes are read as it is.
        var window = app.PrimaryWindow;
        if (Math.Min(window.LayoutWidth, window.LayoutHeight) < 600f) Halide.UsePhoneSizes();

        Func<IMediaBackend>? backends = MpvBackend.IsAvailable ? () => new MpvBackend() : null;
        var settings = new ApertureSettings(CredentialStore.Default);
        var aperture = new ApertureApp(app, app.PrimaryWindow, settings, Demo ? DemoLibrary.Make() : null, backends);
        app.SetRoot(aperture.Mount());
        app.Run();
        aperture.Dispose();
    }
}

/// <summary>
/// A made-up library for looking at the app with no server: made-up titles, no artwork. Its "server"
/// is a folder of the app's own, so a film plays what is put where its stream would be:
/// <c>files/demo/Videos/&lt;id&gt;/stream?static=true</c> (the fake's stream address, a file URL here),
/// as scripts put a test clip there.
/// </summary>
internal static class DemoLibrary
{
    public static IMediaLibrary Make()
    {
        string folder = Path.Combine(global::Android.App.Application.Context.FilesDir!.AbsolutePath, "demo");
        var library = new FakeMediaLibrary(new ServerInfo("file://" + folder, "Demo library", "12.1.0", "demo"))
            .AddLibrary("films", "Films", LibraryKind.Movies)
            .AddLibrary("shows", "Series", LibraryKind.TvShows);
        string[] films =
        [
            "Northern Lights", "The Quiet Harbour", "Paper Kites", "A Long Way Down", "Lanterns",
            "The Glass Orchard", "Midnight Ferry", "Salt and Iron", "Winter Station", "The Last Signal",
            "Copper Sky", "Small Hours",
        ];
        for (int i = 0; i < films.Length; i++)
            library.AddMovie("f" + i, films[i], "films", 1990 + i * 3, 88 + i * 7,
                resume: i == 1 ? TimeSpan.FromMinutes(40) : null, played: i % 4 == 3);
        string[] shows = ["Harbour Lights", "The Cartographers", "Static"];
        for (int i = 0; i < shows.Length; i++)
        {
            library.AddItem(new MediaItem
            {
                Id = "s" + i, Name = shows[i], Kind = MediaKind.Series, PrimaryImageTag = "tag-s" + i,
                ChildCount = 2, UnplayedItemCount = 6, Overview = $"{shows[i]} is made up.",
            }, "shows");
            library.AddSeason("s" + i, 1, 8, watched: 3).AddSeason("s" + i, 2, 6);
        }
        library.ResumeIds.Add("f1");
        library.NextUpIds.Add("s0-s1e4");
        return library;
    }
}
