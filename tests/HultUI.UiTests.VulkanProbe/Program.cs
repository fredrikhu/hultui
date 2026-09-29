using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;

// Opens a window the way an app asks for Vulkan, in code, so a start that fails may fall back to
// GL. It draws a fixed number of forced frames and exits, so HULTUI_VULKAN_CAPTURE's default frame
// (the first to reuse a swapchain image) is always reached. The left half is red and the right
// half blue, so a capture can be checked for more than "not black".
//
// --renderer=default asks for nothing, --frames=N changes the frame count (12).

var renderer = args.Contains("--renderer=default") ? RendererKind.Default : RendererKind.Vulkan;
int frames = args.FirstOrDefault(a => a.StartsWith("--frames=")) is { } f ? int.Parse(f["--frames=".Length..]) : 12;

using var app = new Application("vulkan-probe", 320, 200, renderer: renderer);
app.SetRoot(Flex.Row().Width(320f).Height(200f).Children(
    Box.New().FlexGrow(1).Background(Color.FromHex("#FF0000")),
    Box.New().FlexGrow(1).Background(Color.FromHex("#0000FF"))));

for (int i = 0; i < frames; i++)
    app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true);

Console.WriteLine("probe: done");
