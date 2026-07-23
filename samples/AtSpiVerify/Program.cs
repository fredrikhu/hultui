using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Headless;
using HultUI.Platform.Accessibility.Linux;

using Text = HultUI.Core.Builders.Text;

// Drives a real HultUI window through the headless pump — no display, no GL —
// with the AT-SPI2 bridge swapped in, so the accessible tree is exported to the
// live a11y bus for inspection with pyatspi / accerciser / orca.

// ATSPI_SCALE lets us verify device-pixel extents (default 1×).
float scale = float.TryParse(Environment.GetEnvironmentVariable("ATSPI_SCALE"), out var s) ? s : 1f;
using var session = HeadlessSession.Create(width: 400, height: 300, scale: scale);
Console.WriteLine($"[harness] display scale = {scale}");

int count = 0;
var counter = Text.New("Count: 0");
var button = Button.New("Increment");
var checkbox = Checkbox.New(false, "Enable feature");
// The framework only infers a Button role for a clickable element, so a real
// click handler is what makes this export as a "push button".
button.OnClick(_ => counter.Content = $"Count: {++count}");

// A named List container so a client can find it by role+name and count its
// children across bulk add/remove cycles (used by ATSPI_BULK verification).
var list = Flex.Column(gap: 2).AccessibleRole(AccessibilityRole.List).AccessibleName("bulk-list");

var column = Flex.Column(gap: 8).Padding(12).Children(
    Text.New("HultUI AT-SPI Verify"),
    button,
    checkbox,
    counter,
    list);
session.Window.SetRoot(column);

var actionHost = session.Window.InstallActionRouter();
var bridge = session.Window.InstallAccessibilityBridge(new LinuxAccessibilityBridge(actionHost));
session.PumpFrame();

Console.WriteLine($"[harness] bridge.IsActive = {bridge.IsActive}");
if (!bridge.IsActive)
{
    Console.WriteLine("[harness] AT-SPI bus not reachable / Embed failed — nothing to inspect. Exiting.");
    return;
}

Console.WriteLine("[harness] Exported to the a11y bus. Toolkit name = 'HultUI'.");

// ── Bulk mode (ATSPI_BULK=1): cycle the named List through empty → N children →
//    empty so a *live* pyatspi client (see atspi_bulk_verify.py) can confirm it
//    observes the correct child count after the coalesced children-changed
//    signals. This is the real-client check for the event-coalescing change:
//    per-node cache add/remove + one children-changed nudge per parent must keep
//    a caching client's view of the container correct.
if (Environment.GetEnvironmentVariable("ATSPI_BULK") == "1")
{
    int n = int.TryParse(Environment.GetEnvironmentVariable("ATSPI_BULK_N"), out var pn) ? pn : 200;
    Console.WriteLine($"[harness] BULK mode: cycling '{list.AccessibleName}' 0 ⇄ {n} children every 3s. Ctrl+C to stop.");
    bool bstop = false;
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; bstop = true; };
    int cycle = 0;
    while (!bstop)
    {
        // Fill.
        for (int i = 0; i < n; i++)
            list.AddChild(Box.New().AccessibleRole(AccessibilityRole.ListItem)
                .AccessibleName($"item {i}").Children(Text.New($"item {i}")));
        session.PumpFrame();
        Console.WriteLine($"[harness] cycle {cycle} FILLED  → EXPECT list children={n} (applyChanges={bridge.DebugApplyChangesCalls})");
        for (int t = 0; t < 10 && !bstop; t++) { Thread.Sleep(300); session.PumpFrame(); }

        // Empty.
        while (list.Children.Count > 0) list.RemoveChild(list.Children[^1]);
        session.PumpFrame();
        Console.WriteLine($"[harness] cycle {cycle} EMPTIED → EXPECT list children=0 (applyChanges={bridge.DebugApplyChangesCalls})");
        for (int t = 0; t < 10 && !bstop; t++) { Thread.Sleep(300); session.PumpFrame(); }
        cycle++;
    }
    Console.WriteLine("[harness] Stopped.");
    return;
}

Console.WriteLine("[harness] Running. Ctrl+C to stop. Emitting a state change every 2s.");

// Keep the process alive and generate periodic change signals so an AT client
// can observe children-changed / state-changed / property-change traffic.
// Everything stays on this (main) thread: PumpFrame dispatches to the thread it
// was first pumped on, so a synchronous loop avoids a cross-thread deadlock.
bool toggled = false;
bool stop = false;
int tick = 0;
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop = true; };

while (!stop)
{
    Thread.Sleep(300);
    if (stop) break;

    // Pump each iteration so an AT-triggered DoAction the client queued gets
    // drained (and its click dispatched) promptly. The counter is driven ONLY
    // by button clicks, so its value == how many times an AT invoked the button.
    session.PumpFrame();

    if (++tick % 10 == 0)
    {
        toggled = !toggled;
        checkbox.IsChecked = toggled; // periodic state-change for signal testing

        // Reorder: move the button to the end, then back — a same-set, new-order
        // change that produces a TreeDiff.Reordered.
        column.RemoveChild(button);
        if (toggled) column.AddChild(button); else column.InsertChild(1, button);
        session.PumpFrame();
        Console.WriteLine($"[harness] button-invokes={count} checkbox={toggled} " +
            $"button-at={(toggled ? "end" : "index1")} applyChanges={bridge.DebugApplyChangesCalls}");
    }
}

Console.WriteLine("[harness] Stopped.");
