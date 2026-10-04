# XTerm.NET patches

Four fixes the spike found XTerm.NET needs, as a `git format-patch` series against
[tomlm/XTerm.NET](https://github.com/tomlm/XTerm.NET) `56ea809` (2.0.2 plus one merged PR). With
them, all 2 238 of XTerm.NET's own tests pass (2 221 that were there, 17 new in
`Buffer/CursorLineReflowTests.cs`, added by 0004).

**They stay HultUI's patches and are not offered upstream** (decided 2026-10-03). HultUI.Terminal
builds XTerm.NET from upstream source at a pinned commit with this series applied
([docs/design/TERMINAL.md](../../../docs/design/TERMINAL.md), *The patched engine*). Its step 1
moves the series to `external/XTerm.NET.patches/`, where it is maintained from then on; this copy
is the measured original.

| Patch | Fixes | Measured |
|---|---|---|
| [0001](0001-A-width-change-reuses-each-line-s-cell-array-instead.patch) | A width change reallocated every line of the buffer | 25 → 3.6 ms per one-column change at 10 000 × 200 |
| [0002](0002-SGR-53-and-55-set-and-clear-overline.patch) | SGR 53/55 never reached a cell | behaviour 39 → 40 of 40 |
| [0003](0003-Alt-prefixes-Enter-and-Backspace-Ctrl-Backspace-is-B.patch) | Alt+Enter, Alt+Backspace, Ctrl+Backspace, Ctrl+/ | keys 45 → 49 of 49 |
| [0004](0004-ReflowCursorLine-the-wrapped-line-holding-the-cursor.patch) | `TerminalOptions.ReflowCursorLine`: the cursor's line reflows too (default off) | pwsh's prompt stays whole through ConPTY after a narrowing |

```bash
git clone https://github.com/tomlm/XTerm.NET xtermnet && cd xtermnet
git checkout 56ea809
git am ../spikes/XtermNet/patches/*.patch
dotnet test src/XTerm.NET.Tests -c Release
```
