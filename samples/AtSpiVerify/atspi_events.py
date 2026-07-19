#!/usr/bin/env python3
"""Listen for AT-SPI change signals from the HultUI app for a few seconds."""
import gi
gi.require_version("Atspi", "2.0")
from gi.repository import Atspi, GLib

seen = []

def on_event(e):
    src = e.source
    try:
        tk = src.get_application().get_toolkit_name()
    except Exception:
        tk = None
    if tk != "HultUI":
        return
    try:
        name = src.get_name()
        role = src.get_role_name()
    except Exception:
        name, role = "?", "?"
    line = f"{e.type:40s} detail1={e.detail1} src=({role!r} {name!r})"
    seen.append(line)
    print(line, flush=True)

def main():
    Atspi.init()
    for t in ("object:state-changed",
              "object:property-change",
              "object:children-changed"):
        Atspi.EventListener.new(on_event).register(t)
    print("Listening 6s for HultUI events…", flush=True)
    GLib.timeout_add_seconds(6, lambda: (Atspi.event_quit(), False)[1])
    Atspi.event_main()
    print(f"\nTotal HultUI events captured: {len(seen)}")

if __name__ == "__main__":
    main()
