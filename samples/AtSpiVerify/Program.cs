using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Headless;
using HultUI.Platform.Accessibility.Linux;

using Text = HultUI.Core.Builders.Text;

// Drives a real HultUI window through the headless pump — no display, no GL —
// with the AT-SPI2 bridge swapped in, so the accessible tree is exported to the
// live a11y bus for inspection with pyatspi / accerciser / orca.

using var session = HeadlessSession.Create(width: 400, height: 300);

int count = 0;
var counter = Text.New("Count: 0");
var button = Button.New("Increment");
var checkbox = Checkbox.New(false, "Enable feature");
// The framework only infers a Button role for a clickable element, so a real
// click handler is what makes this export as a "push button".
button.OnClick(_ => counter.Content = $"Count: {++count}");

session.Window.SetRoot(Flex.Column(gap: 8).Padding(12).Children(
    Text.New("HultUI AT-SPI Verify"),
    button,
    checkbox,
    counter));

var bridge = session.Window.InstallAccessibilityBridge(new LinuxAccessibilityBridge());
session.PumpFrame();

Console.WriteLine($"[harness] bridge.IsActive = {bridge.IsActive}");
if (!bridge.IsActive)
{
    Console.WriteLine("[harness] AT-SPI bus not reachable / Embed failed — nothing to inspect. Exiting.");
    return;
}

Console.WriteLine("[harness] Exported to the a11y bus. Toolkit name = 'HultUI'.");
Console.WriteLine("[harness] Running. Ctrl+C to stop. Emitting a state change every 2s.");

// Keep the process alive and generate periodic change signals so an AT client
// can observe children-changed / state-changed / property-change traffic.
// Everything stays on this (main) thread: PumpFrame dispatches to the thread it
// was first pumped on, so a synchronous loop avoids a cross-thread deadlock.
bool toggled = false;
bool stop = false;
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop = true; };

while (!stop)
{
    Thread.Sleep(2000);
    if (stop) break;

    toggled = !toggled;
    checkbox.IsChecked = toggled;          // state-change (checked) on the checkbox
    counter.Content = $"Count: {++count}"; // name property-change on the label
    session.PumpFrame();
    Console.WriteLine($"[harness] applyChanges={bridge.DebugApplyChangesCalls} signals={bridge.DebugSignalsEmitted}");
}

Console.WriteLine("[harness] Stopped.");
