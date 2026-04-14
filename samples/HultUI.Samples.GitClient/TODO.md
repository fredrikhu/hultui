# GitClient Sample — Feature Roadmap

Ordered backlog for the GitClient sample. Items are grouped into phases by
risk and dependency, not by importance — finish each phase before starting
the next so the app stays in a usable state at every commit.

**Current cursor:** ▶ Phase 4.5 (File history)

When picking up work: read the cursor, do the next unchecked item, move
the cursor pointer down, check the box. Items inside a phase can be
reordered freely; phases should be done in order because later phases
assume infrastructure built in earlier ones.

---

## Phase 1 — Quick wins (validate patterns, low risk)

These items reuse existing UI patterns (sidebar context menus, status-bar
buttons) and only add small `GitRepository` methods. They're a good warm-up
and unblock useful daily flows.

### 1.1 Stash operations
- [x] `GitRepository.StashCreate(string? message)` → `git stash push -m`
- [x] `GitRepository.StashApply(string ref)` / `StashPop(string ref)` / `StashDrop(string ref)`
- [x] Sidebar: stash node context menu — Apply / Pop / Drop
- [x] StatusBar or StagingArea: "Stash" button when there are unstaged changes
- **Done when:** I can stash my working tree, see the entry appear in the
  sidebar, apply or pop it from the context menu, and drop it.

### 1.2 Tag operations
- [x] `GitRepository.CreateTag(string name, string? commit, string? message)`
- [x] `GitRepository.DeleteTag(string name)`
- [x] CommitRow context menu: "Tag this commit…" (inline name input pattern from Sidebar branch creation)
- [x] Sidebar: tag node context menu — Delete
- **Done when:** I can tag a commit from the history view, see the tag
  badge on that commit, and delete it from the sidebar.

### 1.3 Commit amend
- [x] StagingArea: "Amend last commit" checkbox next to the commit button
- [x] When checked, prefill the message with the previous commit's message and call `git commit --amend`
- **Done when:** Toggling amend changes the staged commit's hash in place
  rather than creating a new one.

### 1.4 Operation result feedback (infra)
- [x] Small toast or status-bar message slot for `GitResult` failures
- [x] Wire all `GitResult`-returning calls in `App.cs` to surface stderr on failure
- **Done when:** A failed push (e.g. non-fast-forward) shows the git error
  text instead of silently doing nothing. This unblocks safer destructive
  operations in Phase 3.

---

## Phase 2 — Diff & history depth

These improve the everyday "look at the changes" experience. They touch
existing components rather than adding new ones, but each is a meaningful
chunk of work.

### 2.1 Per-hunk staging in DiffViewer
- [x] Hunk header gets stage / unstage / discard buttons
- [x] `GitRepository.ApplyPatch(string patch, bool reverse, bool cached)` wrapping `git apply`
- [x] Build a single-hunk patch from a `GitDiffHunk` and apply it
- **Done when:** I can stage one hunk out of three from a modified file
  and the staging area reflects the partial state.

### 2.2 Side-by-side diff toggle
- [x] DiffViewer: unified / side-by-side toggle in its toolbar
- [x] New rendering path that lays old/new in two parallel columns aligned by hunk
- **Done when:** Toggling the mode keeps the same scroll position and
  selection works in both views.

### 2.3 Filter improvements on CommitList
- [x] Path filter (`git log -- <path>`)
- [x] Author filter
- [x] Date range filter
- [x] Filter chips above the search box rather than overloading the single text input
- **Done when:** I can narrow the log to "everything Fredrik touched in
  Components/ this week" without leaving the UI.

---

## Phase 3 — Destructive operations (depend on Phase 1.4)

Each item here can lose work if it goes wrong. **Do not start until 1.4 is
done** — operation feedback + a confirm dialog primitive are prerequisites.

### 3.1 Confirm dialog primitive
- [x] Reusable modal-style confirm with title, body, destructive/safe button styling
- [x] Used by every Phase 3 item and by Sidebar's existing "Delete branch"

### 3.2 Reset
- [x] `GitRepository.Reset(string ref, ResetMode mode)` for soft/mixed/hard
- [x] CommitRow context menu: "Reset current branch to here" with mode submenu
- [x] Confirm dialog warns about working-tree loss for `--hard`
- **Done when:** Right-click → Reset → Hard moves HEAD and clears the
  working tree, with a confirmation prompt first.

### 3.3 Revert
- [x] `GitRepository.Revert(string commitHash)`
- [x] CommitRow context menu: "Revert commit"
- [x] Surface conflict-on-revert via the Phase 1.4 toast
- **Done when:** Reverting a non-conflicting commit creates the inverse
  commit on top of HEAD.

### 3.4 Force push
- [x] StatusBar: hold Shift on the Push button to force-push-with-lease (or a separate menu item)
- [x] Confirm dialog every time
- **Done when:** Force push works and is impossible to trigger by accident.

---

## Phase 4 — Heavy features

These each carry their own UI surface area. Pick one at a time; each is a
multi-day effort.

### 4.1 Merge another branch into current
- [x] `GitRepository.Merge(string ref, MergeOptions options)` (ff/no-ff/squash)
- [x] Sidebar branch context menu: "Merge into current branch"
- [x] On conflict, transition into the conflict state UI (4.2)

### 4.2 Conflict resolution view
- [x] New panel that lists conflicting files and shows three-way diff per file
- [x] Per-hunk "use ours" / "use theirs" / manual edit
- [x] `git add` + `git commit` to finalize, or `git merge --abort` to back out
- **Done when:** I can complete a merge that has conflicts entirely from
  inside the GitClient sample.

### 4.3 Rebase (non-interactive first)
- [x] `GitRepository.Rebase(string onto, RebaseOptions options)`
- [x] Branch context menu: "Rebase current onto…"
- [x] Reuse 4.2 for conflicts
- [x] Interactive rebase deferred to 4.6

### 4.4 Blame view
- [x] `GitRepository.Blame(string path)` parsing porcelain output
- [x] New file-detail panel: each line annotated with commit/author/date
- [x] Click a line → jump to that commit in the history

### 4.5 File history
- [ ] CommitDetail file row context menu: "Show history of this file"
- [ ] Filtered CommitList variant that respects a `--follow` path filter

### 4.6 Interactive rebase
- [ ] Reorder/squash/edit/drop UI on top of `git rebase -i`
- [ ] Custom sequence editor via `GIT_SEQUENCE_EDITOR` env var to drive the todo list
- [ ] Drag-to-reorder or keyboard-driven commit list with action dropdowns
- [ ] Builds on 4.3's non-interactive rebase and 4.2's conflict resolution

---

## Phase 5 — Polish & infrastructure

Don't start until Phase 4 is at a stopping point.

- [ ] Auto-refresh via filesystem watcher on `.git/HEAD`, `.git/index`, working tree
- [ ] Keyboard shortcuts surfaced in a "?" overlay (Ctrl+P for branches, Ctrl+L for log filter, etc.)
- [ ] Push-to / pull-from specific remote+branch dialog
- [ ] Submodules sub-tree in Sidebar
- [ ] Bisect mode

---

## Out of scope (don't pull into this backlog)

These belong to the framework, not the GitClient sample. Track them in
`/TODO.md` instead:
- Diff renderer performance for very large files
- Virtualizing the DiffViewer
- Markdown rendering inside commit messages
