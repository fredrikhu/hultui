#!/usr/bin/env python3
"""Persistent AT-SPI client: watch whether the HultUI app's child order updates."""
import time
import gi
gi.require_version("Atspi", "2.0")
from gi.repository import Atspi


def main():
    Atspi.init()
    desktop = Atspi.get_desktop(0)
    app = None
    for i in range(desktop.get_child_count()):
        a = desktop.get_child_at_index(i)
        if a and a.get_toolkit_name() == "HultUI" and a.get_child_count() > 0:
            app = a
            break
    if app is None:
        print("HultUI app not found"); return

    def order():
        return [app.get_child_at_index(i).get_name()
                for i in range(app.get_child_count())]

    prev = order()
    print("initial order:", prev)
    changes = 0
    for _ in range(20):  # ~10s
        time.sleep(0.5)
        cur = order()
        if cur != prev:
            changes += 1
            print("REORDER OBSERVED:", cur)
            prev = cur
    print(f"\ntotal reorders observed by a persistent client: {changes} "
          f"-> {'PASS' if changes > 0 else 'STALE (signal not picked up)'}")


if __name__ == "__main__":
    main()
