# AtSpiVerify

A desktop verification harness for the Linux AT-SPI2 accessibility bridge
(`HultUI.Platform.Accessibility.Linux`). It drives a real HultUI window through
the **headless** pump — no display, no GL — with the AT-SPI bridge swapped in,
so the accessible tree is exported to the live a11y bus and can be inspected
with the same client stack Orca uses.

Not part of the solution or CI: it needs a running AT-SPI daemon
(`at-spi2-registryd`), which a headless CI runner doesn't have. Run it by hand
when validating the bridge on a real desktop.

## Usage

```sh
# 1. Start the harness (keeps running, emits a change signal every 2s):
dotnet run --project samples/AtSpiVerify

# 2. In another terminal, dump the exported tree (names, roles, states, extents):
python3 samples/AtSpiVerify/atspi_inspect.py

# 3. Or watch live change signals for a few seconds:
python3 samples/AtSpiVerify/atspi_events.py
```

The Python scripts use `gi.repository.Atspi` (pyatspi) — the client library
Orca is built on — so a clean run here is strong evidence the bridge is correct.

## What "good" looks like

`atspi_inspect.py` should print the app with toolkit `HultUI`, a root
`application` node, and children with the right roles (`push button`,
`check box`, `label`), non-zero extents, and `Action`/`Component` interfaces.
`atspi_events.py` should show `state-changed:checked` and
`property-change:accessible-name` events as the harness mutates state.
