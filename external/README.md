# external

Third-party source HultUI builds itself, kept as upstream plus HultUI's patches.

## XTerm.NET

The terminal emulator engine behind HultUI.Terminal (MIT, Tom Laird-McConnell,
https://github.com/tomlm/XTerm.NET). The design is in `docs/design/TERMINAL.md`, "The patched
engine".

| Path | What it is |
|---|---|
| `XTerm.NET.version` | The upstream URL and the pinned commit |
| `XTerm.NET.patches/` | Everything HultUI's engine differs from upstream by, as a `git format-patch` series |
| `XTerm.NET/` | Upstream's `LICENSE`, `src/XTerm.NET` and `src/XTerm.NET.Tests` at that commit with the series applied. **Generated; never edit it by hand.** |

It is built by `src/HultUI.Terminal.XTermNet` (as `HultUI.Terminal.XTermNet.dll`), and upstream's
own suite runs as `tests/HultUI.Terminal.XTermNet.Tests`.

```bash
pwsh scripts/vendor-xterm-net.ps1 -Verify          # is XTerm.NET/ exactly commit + patches? (CI runs this)
pwsh scripts/vendor-xterm-net.ps1                  # rebuild XTerm.NET/ at the pinned commit
pwsh scripts/vendor-xterm-net.ps1 -Commit <sha>    # move to a new upstream commit
```

**To change the engine,** add or edit a patch, then rebuild:

1. Clone upstream at the pinned commit and `git am` the series.
2. Commit the change on top.
3. Export the series with `git format-patch --no-signature <commit>..HEAD -o external/XTerm.NET.patches`.
4. Run the script.

**When a patch stops applying** after `-Commit`, the script keeps its clone and says how to finish
with `-FromClone`.
