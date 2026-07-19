#!/usr/bin/env python3
"""Drive the HultUI app's Action interface (DoAction) and confirm it takes effect."""
import time
import gi
gi.require_version("Atspi", "2.0")
from gi.repository import Atspi


def find(node, pred):
    if pred(node):
        return node
    for i in range(node.get_child_count()):
        c = node.get_child_at_index(i)
        if c is not None:
            r = find(c, pred)
            if r:
                return r
    return None


def main():
    Atspi.init()
    desktop = Atspi.get_desktop(0)
    apps = []
    for i in range(desktop.get_child_count()):
        a = desktop.get_child_at_index(i)
        if a and a.get_toolkit_name() == "HultUI":
            apps.append(a)
    print(f"HultUI apps: {len(apps)}  child counts: {[a.get_child_count() for a in apps]}")
    app = next((a for a in apps if a.get_child_count() > 0), None)
    if app is None:
        print("No populated HultUI app found"); return

    kids = [app.get_child_at_index(i) for i in range(app.get_child_count())]
    for i, c in enumerate(kids):
        print(f"  child[{i}] role={c.get_role_name()!r} name={c.get_name()!r}")
    button = next((c for c in kids if c.get_role_name() in ("push button", "button")), None)
    checkbox = next((c for c in kids if c.get_role_name() == "check box"), None)
    counter = next((c for c in kids if (c.get_name() or "").startswith("Count:")), None)
    if not (button and checkbox and counter):
        print("Missing a control; aborting."); return
    print(f"button={button.get_name()!r} checkbox={checkbox.get_name()!r} counter={counter.get_name()!r}")

    def checked(node):
        return "checked" in [s.value_nick for s in node.get_state_set().get_states()]

    # ── Button: DoAction should dispatch a click → counter increments.
    print(f"button actions: n={Atspi.Action.get_n_actions(button)} "
          f"name={Atspi.Action.get_action_name(button, 0)!r}")
    before = counter.get_name()
    for _ in range(3):
        Atspi.Action.do_action(button, 0)
        time.sleep(0.5)
    after = counter.get_name()
    print(f"counter before={before!r}  after 3x do_action={after!r}  "
          f"-> {'PASS' if after != before else 'NO CHANGE'}")

    # ── Checkbox: DoAction should toggle the checked state.
    was = checked(checkbox)
    Atspi.Action.do_action(checkbox, 0)
    time.sleep(0.5)
    now = checked(checkbox)
    print(f"checkbox checked before={was} after do_action={now}  "
          f"-> {'PASS' if now != was else 'NO CHANGE'}")


if __name__ == "__main__":
    main()
