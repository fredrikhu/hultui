using System.Reflection;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using HultUI.Media;
using HultUI.Media.Testing;
using HultUI.Rendering.Skia;
using SkiaSharp;

namespace PerfSuite;

static partial class Scenarios
{
    /// <summary>
    /// Reel's and Aperture's player, the <see cref="VideoPlayer"/> overlay, on a fake backend in
    /// <c>Application.Run</c>'s own loop (<c>HeadlessSession.RunLoop</c>, on the virtual clock):
    /// what a playing film costs between the frames that show it, and the pointer running along
    /// its scrubber. Each row reports how often the player was built again.
    /// </summary>
    private static IEnumerable<Scenario> MediaScenarios()
    {
        yield return new Scenario("media/video player playing, position 10/s, real loop",
            "A playing VideoPlayer with its bar up: Application.Run's own loop on the virtual clock while the backend publishes a frame every 40 ms and reports its position every 100 ms, as mpv does, for ten reports (all under build); reports the player's builds per position report",
            ctx =>
            {
                var rig = new PlayerRig(ctx, autoHide: false);
                var backend = rig.Backend;
                int events = 0, reports = 0;
                var position = TimeSpan.FromMinutes(42);
                // Posted, as mpv's events are: the next pass of the loop applies them.
                Action arrive = () =>
                {
                    events++;
                    if (events % 2 == 0) backend.PublishFrame();
                    if (events % 5 == 0)
                    {
                        reports++;
                        position += TimeSpan.FromMilliseconds(100);
                        backend.Raise(new BackendEvent.PositionChanged(position));
                    }
                };
                int buildsBefore = 0, reportsBefore = 0, lastAt = 0, frameAtLast = 0;
                return new Op
                {
                    Run = _ =>
                    {
                        buildsBefore = rig.Video.Builds;
                        reportsBefore = reports;
                        lastAt = -1;
                        // Until the frame after the tenth report has been drawn.
                        ctx.Session.RunLoop(() =>
                            {
                                if (lastAt < 0 && reports - reportsBefore >= 10) { lastAt = reports; frameAtLast = rig.Frames; }
                                return lastAt >= 0 && rig.Frames > frameAtLast;
                            },
                            eventEvery: TimeSpan.FromMilliseconds(20),
                            onEvent: () => HultUI.Application.InvokeOnMainThread(arrive));
                    },
                    Frames = 0,
                    Figure = () => (rig.Video.Builds - buildsBefore) / (double)Math.Max(1, reports - reportsBefore),
                    FigureUnit = "builds/report",
                    Check = _ =>
                    {
                        var shown = rig.Player.Position;
                        string elapsed = VideoPlayer.FormatTime(shown, rig.Player.Duration);
                        if (ctx.Window.FindByText(elapsed) is null) return $"the bar does not read {elapsed}";
                        var seek = (SliderElement)ctx.Window.FindByAutomationId("video-seek")!.Element;
                        return Math.Abs(seek.Value - shown.TotalSeconds) > 0.25
                            ? $"the scrubber stands at {seek.Value:0.0} s with the film at {shown.TotalSeconds:0.0} s"
                            : null;
                    },
                };
            }) { Width = 640, Height = 360 };

        yield return new Scenario("media/video player bar fade, real loop",
            "A pointer move over a playing VideoPlayer whose bar has gone: Application.Run's own loop on the virtual clock until the bar has faded in, waited 250 ms and faded out again over 300 ms (all under build); reports the player's builds per fade",
            ctx =>
            {
                var rig = new PlayerRig(ctx, autoHide: true);
                rig.Video.AutoHideDelay = TimeSpan.FromMilliseconds(250);
                // Every step starts from a bar that has gone.
                ctx.Session.RunLoop(() => !rig.Video.ControlsVisible, limit: TimeSpan.FromSeconds(10));
                ctx.Pump();
                var bar = ctx.Window.FindByAutomationId("video-controls")!.Element;
                float brightest = 0f;
                rig.OnFrame = () => brightest = MathF.Max(brightest, bar.Opacity);
                int buildsBefore = 0;
                return new Op
                {
                    Run = step =>
                    {
                        buildsBefore = rig.Video.Builds;
                        brightest = 0f;
                        ctx.Window.MoveMouse(300 + step % 2, 150);
                        ctx.Session.RunLoop(() => !rig.Video.ControlsVisible, limit: TimeSpan.FromSeconds(10));
                    },
                    Frames = 0,
                    Figure = () => rig.Video.Builds - buildsBefore,
                    FigureUnit = "builds/fade",
                    Check = _ =>
                        rig.Video.ControlsVisible ? "the bar is still up"
                        : brightest < 0.99f ? $"the bar came back only to {brightest:0.00}"
                        : bar.Opacity != 0f ? $"the bar went at {bar.Opacity:0.00}, not 0"
                        : null,
                };
            }) { Width = 640, Height = 360 };

        yield return new Scenario("media/scrubber hover, 1 kHz mouse, real loop",
            "The pointer runs along a playing VideoPlayer's scrubber, moving every millisecond as a 1,000 Hz mouse does, and the player previews the time under it: Application.Run's own loop on the virtual clock for 10 frames, then one more (all under build); reports the player's builds per frame drawn",
            ctx =>
            {
                var rig = new PlayerRig(ctx, autoHide: false);
                // The time the player previews (what its Build reads), for the check.
                var previewAt = (Signal<double?>)typeof(VideoPlayer)
                    .GetField("_previewAt", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(rig.Video)!;
                int builds = 0, drawn = 0;
                double expected = 0;
                return new Op
                {
                    Run = step =>
                    {
                        var seek = (SliderElement)ctx.Window.FindByAutomationId("video-seek")!.Element;
                        var track = seek.WindowContentBox;
                        float y = track.Y + track.Height / 2;
                        float x = step % 2 == 0 ? track.X + 40 : track.X + track.Width - 40;
                        float dx = step % 2 == 0 ? 0.5f : -0.5f;
                        int b0 = rig.Video.Builds, f0 = rig.Frames;
                        Action move = () => { x += dx; ctx.Window.MoveMouse(x, y); };
                        ctx.Session.RunLoop(() => rig.Frames - f0 >= 10, eventEvery: TimeSpan.FromMilliseconds(1),
                            onEvent: () => HultUI.Application.InvokeOnMainThread(move));
                        builds = rig.Video.Builds - b0;
                        drawn = rig.Frames - f0;
                        // The frame the last move is shown in.
                        ctx.Pump();
                        float travel = track.Width - 2 * seek.ThumbRadius;
                        expected = seek.Minimum + Math.Clamp((x - track.X - seek.ThumbRadius) / travel, 0, 1) * (seek.Maximum - seek.Minimum);
                    },
                    Frames = 0,
                    Figure = () => builds / (double)Math.Max(1, drawn),
                    FigureUnit = "builds/frame",
                    Check = _ => previewAt.Peek() is { } at && Math.Abs(at - expected) < 0.01 ? null
                        : $"the player previews {previewAt.Peek():0.00} s, not {expected:0.00} s",
                };
            }) { Width = 640, Height = 360 };

        yield return new Scenario("media/scrubber hover with a preview card, 1 kHz mouse, real loop",
            "The scrubber hover row with a still for every minute of the film, as Reel makes them (120 pictures, each its own image): the card above the bar follows the pointer, crossing two or three stills a frame, so its place, picture and time change every frame drawn; Application.Run's own loop on the virtual clock for 10 frames, then one more (all under build); reports the player's builds per frame drawn",
            ctx =>
            {
                TimeSpan? asked = null;
                var stills = PreviewStills.Value;
                var rig = new PlayerRig(ctx, autoHide: false, preview: at =>
                {
                    asked = at;
                    int i = Math.Clamp((int)Math.Round(at.TotalMinutes), 0, stills.Length - 1);
                    return new VideoPreviewFrame(stills[i], null, TimeSpan.FromMinutes(i));
                });
                int builds = 0, drawn = 0;
                double expected = 0;
                return new Op
                {
                    Run = step =>
                    {
                        var seek = (SliderElement)ctx.Window.FindByAutomationId("video-seek")!.Element;
                        var track = seek.WindowContentBox;
                        float y = track.Y + track.Height / 2;
                        float x = step % 2 == 0 ? track.X + 40 : track.X + track.Width - 40;
                        float dx = step % 2 == 0 ? 0.5f : -0.5f;
                        int b0 = rig.Video.Builds, f0 = rig.Frames;
                        Action move = () => { x += dx; ctx.Window.MoveMouse(x, y); };
                        ctx.Session.RunLoop(() => rig.Frames - f0 >= 10, eventEvery: TimeSpan.FromMilliseconds(1),
                            onEvent: () => HultUI.Application.InvokeOnMainThread(move));
                        builds = rig.Video.Builds - b0;
                        drawn = rig.Frames - f0;
                        // The frame the last move is shown in.
                        ctx.Pump();
                        float travel = track.Width - 2 * seek.ThumbRadius;
                        expected = seek.Minimum + Math.Clamp((x - track.X - seek.ThumbRadius) / travel, 0, 1) * (seek.Maximum - seek.Minimum);
                    },
                    Frames = 0,
                    Figure = () => builds / (double)Math.Max(1, drawn),
                    FigureUnit = "builds/frame",
                    Check = _ =>
                    {
                        if (asked is not { } at || Math.Abs(at.TotalSeconds - expected) > 0.01)
                            return $"the player asked for a still at {asked?.TotalSeconds:0.00} s, not {expected:0.00} s";
                        var card = ctx.Window.FindByAutomationId("video-preview-card")?.Element;
                        if (card is null) return "no preview card";
                        int still = Math.Clamp((int)Math.Round(expected / 60), 0, stills.Length - 1);
                        string time = VideoPlayer.FormatTime(TimeSpan.FromMinutes(still), TimeSpan.FromMinutes(still));
                        string? shown = card.Children.OfType<TextElement>().FirstOrDefault()?.Content;
                        if (shown != time) return $"the card reads {shown}, not {time}";
                        var picture = card.Children.OfType<ImageElement>().FirstOrDefault();
                        if (!ReferenceEquals(picture?.Source, stills[still])) return "the card shows another still";
                        // Centred over the point being previewed, kept inside the player.
                        var root = ctx.Window.FindByAutomationId("video-player")!.Element.Layout.AbsoluteContentBox;
                        var trackBox = ctx.Window.FindByAutomationId("video-seek")!.Element.Layout.AbsoluteContentBox;
                        float cardWidth = card.Layout.AbsoluteBorderBox.Width;
                        float centre = trackBox.X - root.X + (float)(expected / rig.Backend.Duration!.Value.TotalSeconds) * trackBox.Width;
                        float left = Math.Clamp(centre - cardWidth / 2, 4f, root.Width - cardWidth - 4f);
                        float stands = card.Layout.AbsoluteBorderBox.X - root.X;
                        return Math.Abs(stands - left) > 1f ? $"the card stands at {stands:0.0}, not {left:0.0}" : null;
                    },
                };
            }) { Width = 640, Height = 360 };
    }

    /// <summary>A still for every minute of a two-hour film, each its own small picture in its own
    /// colour, as Reel's background decoder makes them; made once for every run.</summary>
    private static readonly Lazy<IImageSource[]> PreviewStills = new(() =>
    {
        var stills = new IImageSource[120];
        for (int i = 0; i < stills.Length; i++)
        {
            var bitmap = new SKBitmap(96, 54);
            bitmap.Erase(new SKColor((byte)(i * 2), (byte)(255 - i * 2), (byte)(i * 37 % 256)));
            stills[i] = new SkiaImageSource(bitmap);
        }
        return stills;
    });

    /// <summary>A playing <see cref="VideoPlayer"/> filling a 640 × 360 window, a two-hour film on a
    /// <see cref="FakeMediaBackend"/>, and a one-pixel canvas that counts the frames drawn.</summary>
    private sealed class PlayerRig
    {
        public FakeMediaBackend Backend { get; } = new() { Duration = TimeSpan.FromHours(2) };
        public MediaPlayer Player { get; }
        public VideoPlayer Video { get; }
        public int Frames { get; private set; }
        public Action? OnFrame { get; set; }

        public PlayerRig(Ctx ctx, bool autoHide, Func<TimeSpan, VideoPreviewFrame?>? preview = null)
        {
            Player = new MediaPlayer(Backend);
            Video = new VideoPlayer(Player) { AutoHideControls = autoHide, PreviewFrame = preview };
            ctx.Mount(Flex.Column().FlexGrow(1).Children(
                Video.Mount().FlexGrow(1),
                Canvas.New(_ => { Frames++; OnFrame?.Invoke(); }).Width(Dimension.Px(1)).Height(Dimension.Px(1))));
            Player.Open(MediaSource.FromFile("film.mkv"), autoPlay: true);
            Player.Seek(TimeSpan.FromMinutes(42));
            ctx.Pump(3);
        }
    }
}
