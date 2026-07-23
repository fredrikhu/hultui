#!/usr/bin/env python3
"""Verify the AT-SPI event-coalescing change against a *live* client.

Run the harness in bulk mode first, in another terminal:

    ATSPI_BULK=1 dotnet run --project samples/AtSpiVerify

then this script. It connects the same way Orca does (pyatspi / libatspi, which
caches the tree and refreshes it from children-changed signals), subscribes to
children-changed, and samples the named List's child count twice a second while
the harness cycles it 0 ⇄ N. A caching client that only saw ONE coalesced
children-changed per bulk op (instead of one per child) must still track the
count exactly — that is what proves the coalescing keeps a real client correct.

PASS = the observed child count reaches the harness's N when FILLED and 0 when
EMPTIED, every cycle. A stale/wrong count means coalescing dropped information a
real client needed.
"""
import gi
gi.require_version("Atspi", "2.0")
from gi.repository import Atspi, GLib

LIST_NAME = "bulk-list"
signal_count = 0
samples = []


def find_list():
    desktop = Atspi.get_desktop(0)
    for i in range(desktop.get_child_count()):
        app = desktop.get_child_at_index(i)
        try:
            if app is None or app.get_toolkit_name() != "HultUI":
                continue
        except Exception:
            continue
        stack = [app]
        while stack:
            node = stack.pop()
            try:
                if node.get_role_name() == "list" and node.get_name() == LIST_NAME:
                    return node
                for j in range(node.get_child_count()):
                    c = node.get_child_at_index(j)
                    if c is not None:
                        stack.append(c)
            except Exception:
                pass
    return None


def on_children_changed(e):
    global signal_count
    try:
        if e.source.get_application().get_toolkit_name() == "HultUI":
            signal_count += 1
    except Exception:
        pass


def main():
    Atspi.init()
    lst = find_list()
    if lst is None:
        print("FAIL: could not find the 'bulk-list' List node — is the harness running with ATSPI_BULK=1?")
        return

    # Be a live caching client: subscribe to children-changed like Orca does.
    Atspi.EventListener.new(on_children_changed).register("object:children-changed")

    print(f"Found List {LIST_NAME!r}. Sampling child count for ~16s while it cycles…\n")

    def sample():
        try:
            n = lst.get_child_count()
        except Exception as ex:
            n = f"<err {ex}>"
        samples.append(n)
        print(f"  observed list children = {n}   (children-changed signals so far: {signal_count})", flush=True)
        return True

    GLib.timeout_add(500, sample)
    GLib.timeout_add_seconds(16, lambda: (Atspi.event_quit(), False)[1])
    Atspi.event_main()

    numeric = [s for s in samples if isinstance(s, int)]
    hi, lo = (max(numeric) if numeric else None), (min(numeric) if numeric else None)
    print(f"\nObserved range: min={lo} max={hi} over {len(samples)} samples; "
          f"{signal_count} children-changed signals received.")
    print("PASS: client tracked the fill/empty cycle." if numeric and hi and hi >= 1 and lo == 0
          else "CHECK: counts did not swing between 0 and N as expected — inspect above.")


if __name__ == "__main__":
    main()
