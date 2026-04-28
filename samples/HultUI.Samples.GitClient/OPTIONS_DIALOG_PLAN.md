# GitClient Options Dialog Plan

This plan captures the next settings/options work for the GitClient sample.
It is intentionally implementation-oriented so it can be picked up later
without redoing discovery.

## Current State

- Implemented slices:
  - Settings groups and options dialog shell.
  - Appearance tab with dark/light theme switching.
  - Git tab pull behavior for merge/rebase/fast-forward-only, auto-stash, and prune.
  - General tab auto-fetch interval for current repository or all open repositories.
  - External Tools tab for VS Code/custom diff command plus `Ctrl+D` launch from
    Working Copy and Commit Details.
- Settings live in `GitClientSettingsData` and now persist repository tabs plus
  `General`, `Git`, `ExternalTools`, and `Appearance` option groups.
- `TabbedApp.PersistTabs()` updates repository fields on the shared settings
  object, preserving option groups when tab state changes.
- Pull now goes through `GitRepository.PullAsync()` / `PullFromAsync()` with
  `PullOptions`.
- Repository watching already exists through `GitWatcher`, with a hard-coded
  300 ms debounce. Timed auto-fetch is owned by `TabbedApp`.
- The open repository flow uses `OpenRepositoryDialog`, a modal with a text
  input path field.
- `GitClientColors` now delegates to the active dark/light palette.

## Options Dialog Shape

Use four initial tabs:

- `General`
- `Git`
- `External Tools`
- `Appearance`

Leave room for later `Keyboard` and `Diff` tabs, but do not introduce empty
tabs in the first pass.

### General Tab

#### Auto Fetch Interval

Fields:

- Enable auto-fetch: `bool AutoFetchEnabled`
- Interval minutes: `int AutoFetchIntervalMinutes`
- Fetch mode: `AutoFetchMode CurrentRepository | AllOpenRepositories`

Default:

- Disabled by default.
- Interval default: 5 minutes.
- Mode default: current repository only.

Implementation notes:

- Add a timer owned by `TabbedApp`, not each `App`, if the chosen mode can
  fetch all open repositories.
- Reuse `GitRepository.FetchAsync()` so credential prompts keep working.
- Do not run auto-fetch while `_remoteOpRunning` is true for that repo.
- Surface failures through the existing status message path, but avoid noisy
  success toasts unless the fetch changed state.
- Pause the timer while an options dialog is open only if it causes re-entry
  issues.

Acceptance:

- Enabling auto-fetch persists across restart.
- Changing the interval takes effect without restarting.
- Auto-fetch never overlaps manual fetch/pull/push on the same repository.

### Git Tab

#### Pull Behavior

Fields:

- Pull strategy: `PullStrategy Merge | Rebase | FastForwardOnly`
- Auto-stash before rebase/pull: `bool PullAutoStash`
- Prune during fetch/pull: `bool PruneOnFetch`

Default:

- Pull strategy: `Rebase`.
- Auto-stash: enabled when strategy is `Rebase`, disabled otherwise.
- Prune: enabled, matching the existing `fetch --all --prune` behavior.

Implementation notes:

- Add a small options object passed into `GitRepository.PullAsync()` and
  `PullFromAsync()`.
- Command mapping:
  - `Merge`: `git pull`
  - `Rebase`: `git pull --rebase`
  - `FastForwardOnly`: `git pull --ff-only`
  - Auto-stash: add `--autostash` when enabled.
  - Prune: add `--prune` for pull if the git version supports it; otherwise
    perform `fetch --prune` before pull.
- Keep the pull-from-specific-remote flow using the same strategy.
- Show the chosen strategy in the options UI, not in the title bar.

Acceptance:

- Plain Pull honors the configured strategy.
- Pull From remote/branch honors the same strategy.
- Rebase conflicts still transition into the existing rebase/conflict UI.

### External Tools Tab

#### External Diff Tool

Fields:

- Diff tool preset: `ExternalToolPreset None | VSCode | VisualStudio | Rider |
  BeyondCompare | Meld | KDiff3 | Custom`
- Diff executable path: `string ExternalDiffToolPath`
- Diff arguments: `string ExternalDiffToolArgs`
- Merge executable path: `string ExternalMergeToolPath` (can be blank in the
  first UI pass if merge launch is not wired yet)
- Merge arguments: `string ExternalMergeToolArgs`

Default presets:

- VS Code diff: `code --wait --diff {left} {right}`
- VS Code merge placeholder: `code --wait {merged}`

Placeholders:

- `{repo}` repository root
- `{path}` relative file path
- `{left}` old/base temp file
- `{right}` new/current temp file
- `{base}` common ancestor temp file, when available
- `{merged}` working tree file for merge/conflict tools

Implementation notes:

- Add a command launcher helper that expands placeholders and starts the
  configured process.
- For commit details, create temp files from the selected commit and parent
  version of the selected path.
- For working copy, compare index/HEAD/worktree depending on whether the
  selected change is staged or unstaged.
- Add `Ctrl+D` handling where file selection already lives:
  - Working Copy selected file in `StagingArea`
  - Commit Details selected file in `CommitDetail`
- Add a small "Test" action in the options UI that validates executable
  existence and argument expansion, without requiring a real selected file.
- Keep temp files alive until the external process exits.

Acceptance:

- With VS Code configured, `Ctrl+D` opens a selected working-copy file in
  external diff.
- The same shortcut works on a selected file in commit details.
- Missing executable produces a visible error, not a silent failure.
- Custom arguments are persisted and survive restart.

### Appearance Tab

#### Light Theme

Fields:

- Theme: `ThemeMode System | Dark | Light`

Default:

- `Dark`, preserving the current application appearance.

Implementation notes:

- Replace static-only color access with a theme palette instance or provider.
- Keep `GitClientColors` as the compatibility entry point if possible, but
  make it delegate to the active palette.
- Define full dark and light palettes, including diff colors, status colors,
  selection, buttons, and dialog backdrop/panel colors.
- Apply theme changes immediately by marking all open tabs dirty.
- Do not change layout dimensions as part of this work.

Acceptance:

- Light theme can be selected and persists across restart.
- Theme changes apply without reopening the app.
- Diff addition/deletion/hunk colors remain readable in both themes.

## Settings Infrastructure Tasks

1. Expand `GitClientSettingsData` with option groups:
   - `GeneralOptions`
   - `GitOptions`
   - `ExternalToolOptions`
   - `AppearanceOptions`
2. Keep defaults in constructors/property initializers so old settings files
   deserialize safely.
3. Add a settings service or shared settings reference so `TabbedApp` can save
   repository tabs without dropping options.
4. Add an options dialog component under `Components/Shared` or
   `Components/Layout`.
5. Add an options button/menu entry in the title bar or tab strip.
6. Add tests around settings round-trip/default compatibility if there are
   sample tests available; otherwise verify with `dotnet build`.

## Later Feature Plan: Word Diff / Intraline Highlight

Goal:

- Highlight changed words or spans inside modified lines, in both unified and
  side-by-side modes.

Current integration points:

- `GitRepository.ParseUnifiedDiff()` produces line-level `GitDiffLine`.
- `DiffViewer.FlattenUnified()` and `FlattenSideBySide()` render each line as
  a single `TextElement`.

Plan:

1. Add a `DiffHighlightSpan` model with start, length, and kind.
2. Extend display-line models to carry highlight spans for left and right
   content.
3. Implement an intraline diff algorithm for paired deletion/addition lines.
   Prefer a small, deterministic LCS/Myers implementation scoped to line text;
   do not depend on `git --word-diff` for the first pass because that output
   changes parser shape and complicates patch reconstruction.
4. Compute spans during `FlattenSideBySide()` where deletion/addition pairs
   are already matched.
5. For unified mode, pair contiguous delete/add blocks in the same way and
   attach spans to each affected line.
6. Teach the renderer to emit segmented text. If the current text element
   cannot style spans, add a focused rich text/run element to HultUI or render
   small text boxes in a row as a stopgap.
7. Add an option later under a `Diff` tab:
   - Intraline highlighting: `Off | Words | Characters`

Acceptance:

- Simple single-line edits highlight only changed words.
- Insertions and deletions in side-by-side mode highlight the corresponding
  changed spans on both sides.
- Rendering remains usable for large diffs; skip span calculation above a
  configurable line length if necessary.

## Later Feature Plan: Native File Browser For Opening Repository

Goal:

- Replace or augment the repository-path textbox with a native folder picker
  when the platform supports it.

Current integration point:

- `OpenRepositoryDialog` owns the textbox and calls
  `RepositoryPathResolver.FindGitRoot()` after confirmation.

Plan:

1. Check what SDL3/HultUI exposes for native file/folder dialogs.
2. If a native folder picker exists, add a `Browse` button beside the path
   input and keep the textbox for manual paste/edit.
3. If no native API exists, introduce a small platform abstraction:
   `IFolderPicker.PickFolderAsync(initialDirectory)`.
4. Implement supported platforms first:
   - Windows: native folder picker.
   - Linux: GTK/portal if available, otherwise fallback to textbox.
   - macOS: native folder picker if the platform backend supports it.
5. After folder selection, run the existing `FindGitRoot()` validation.
6. Keep `Ctrl+O` opening the dialog; focus should land on the Browse button
   only if the textbox is no longer primary. Otherwise preserve textbox focus.

Acceptance:

- Clicking Browse opens a native directory picker on supported platforms.
- Selecting a subdirectory inside a repo opens the repository root.
- Unsupported platforms still work through manual path entry.
- Error messages remain inside the existing dialog.

## Suggested Implementation Order

1. Settings infrastructure and options dialog shell.
2. Appearance tab with light theme, because it exercises app-wide settings
   propagation without git side effects.
3. Git tab pull behavior.
4. General tab auto-fetch interval.
5. External Tools tab and `Ctrl+D` external diff launch.
6. Native file browser.
7. Word diff/intraline highlight.
