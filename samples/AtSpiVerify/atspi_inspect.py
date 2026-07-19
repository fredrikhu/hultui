#!/usr/bin/env python3
"""Inspect the HultUI app on the AT-SPI bus using the same client stack Orca uses."""
import gi
gi.require_version("Atspi", "2.0")
from gi.repository import Atspi

def dump(node, depth=0):
    try:
        name = node.get_name()
        role = node.get_role_name()
        n = node.get_child_count()
        try:
            ext = node.get_extents(Atspi.CoordType.WINDOW)
            box = f"[{ext.x},{ext.y} {ext.width}x{ext.height}]"
        except Exception as e:
            box = f"[extents err: {e}]"
        states = sorted(s.value_nick for s in node.get_state_set().get_states())
        keep = [s for s in states if s in
                ("enabled","sensitive","showing","visible","focusable","focused","checked","checkable","selectable")]
        ifaces = node.get_interfaces()
        print("  " * depth + f"{role!r} name={name!r} {box} states={keep} ifaces={ifaces}")
        for i in range(n):
            child = node.get_child_at_index(i)
            if child:
                dump(child, depth + 1)
    except Exception as e:
        print("  " * depth + f"<error: {e}>")

def main():
    Atspi.init()
    desktop = Atspi.get_desktop(0)
    found = False
    for i in range(desktop.get_child_count()):
        app = desktop.get_child_at_index(i)
        if app is None:
            continue
        try:
            tk = app.get_toolkit_name()
        except Exception:
            tk = None
        if tk == "HultUI":
            found = True
            print(f"=== Found HultUI application: name={app.get_name()!r} toolkit={tk} "
                  f"version={app.get_toolkit_version()} ===")
            dump(app)
    if not found:
        print("HultUI application NOT found on the a11y bus. Apps present:")
        for i in range(desktop.get_child_count()):
            app = desktop.get_child_at_index(i)
            if app:
                try:
                    print(f"  - {app.get_name()!r} toolkit={app.get_toolkit_name()!r}")
                except Exception as e:
                    print(f"  - <error {e}>")

if __name__ == "__main__":
    main()
