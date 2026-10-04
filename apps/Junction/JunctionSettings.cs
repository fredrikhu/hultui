using System.Text.Json.Serialization;
using HultUI.Core.Storage;

namespace Junction;

public sealed class JunctionSettingsData
{
    public List<string> RepositoryPaths { get; set; } = [];
    public int ActiveRepositoryIndex { get; set; }

    // Junction: repositories are grouped into user-defined workspaces that are
    // opened together. RepositoryPaths is retained as the flat set of every
    // known repo (and for back-compat migration into a default workspace).
    public List<WorkspaceData> Workspaces { get; set; } = [];
    public string? ActiveWorkspaceId { get; set; }
    public string? ActiveRepoPath { get; set; }

    /// <summary>Whether the diff wraps long lines rather than scrolling sideways to them.
    /// Remembered because it is a reading preference, not a per-file decision: someone who
    /// wants wrapping wants it for every diff they open, and having to press w each time is
    /// the kind of small friction that makes a feature go unused.</summary>
    public bool DiffWrap { get; set; }

    /// <summary>Whether the diff shows the two sides side by side rather than as one
    /// unified column. Remembered alongside the wrap choice: both are how someone prefers
    /// to read a diff, not decisions about a particular one.</summary>
    public bool DiffSplit { get; set; }

    /// <summary>Whether the Branches and Tags tables group refs into a folder tree by their
    /// <c>/</c> segments rather than listing them flat. Remembered for the same reason the
    /// diff's reading modes are: it is how someone wants to look at their refs, not a
    /// decision about one repository. Which folders are open is not stored — that is a
    /// place in a list, and it is per-repo session state.</summary>
    public bool RefTreeView { get; set; } = true;

    /// <summary>Whether the Commit tab shows the body of the commit message under the
    /// subject. Remembered for the same reason the diff's reading modes are: someone who
    /// wants the message out of the way wants that for every commit they open, not for
    /// one. The subject is always shown — collapsing hides the body, not the message.</summary>
    public bool CommitMessageExpanded { get; set; } = true;

    /// <summary>Widths of the file list beside the diff in the Commit and Changes tabs,
    /// as the user last dragged them. Two figures rather than one: the Changes list carries
    /// the commit composer under it and wants more room than a commit's flat file list.
    /// Clamped on load — a settings file edited by hand should not be able to leave the
    /// splitter somewhere it cannot be dragged back from.</summary>
    public float CommitFileListWidth { get; set; } = 238f;
    public float ChangesFileListWidth { get; set; } = 330f;

    // Junction: per-repo badge swatch id (path → swatch id), driving the repo's
    // initial tile everywhere. Missing entries fall back to a deterministic seed.
    public Dictionary<string, string> RepoBadges { get; set; } = new();

    /// <summary>The <c>/</c>-terminated prefix of the last branch created — <c>feature/</c>,
    /// <c>fredrik/fix/</c> — used to seed the next New branch prompt. A team's branches almost
    /// always share a root, and retyping it is the whole of the friction in naming a branch.
    /// Empty when the last branch had no prefix, which is itself the answer: someone who
    /// creates <c>hotfix</c> at the top level gets an empty field next time, not a stale root
    /// they have to clear.</summary>
    public string LastBranchRoot { get; set; } = "";

    public GeneralOptions General { get; set; } = new();
    public GitOptions Git { get; set; } = new();
    public ExternalToolOptions ExternalTools { get; set; } = new();
    public AppearanceOptions Appearance { get; set; } = new();

    /// <summary>Signed-in GitLab identities — who and where, never the token (that is in
    /// the OS credential store; see <see cref="Junction.GitLab.GitLabAccounts"/>).</summary>
    public List<Junction.GitLab.GitLabAccountData> GitLabAccounts { get; set; } = [];

    /// <summary>Hosts whose first-run "Connect to …" strip was dismissed. Cleared for a host
    /// on sign-out, so the strip comes back if the account is removed later.</summary>
    public List<string> GitLabDismissedHosts { get; set; } = [];

    /// <summary>Per repository (normalised path), the remote whose URL says where it is
    /// hosted, when it is not <c>origin</c>. Absent means origin, then the only remote.</summary>
    public Dictionary<string, string> RepoHostingRemote { get; set; } = new();

    public void Normalize()
    {
        RepositoryPaths ??= [];
        Workspaces ??= [];
        RepoBadges ??= new();
        LastBranchRoot ??= "";
        General ??= new GeneralOptions();
        Git ??= new GitOptions();
        ExternalTools ??= new ExternalToolOptions();
        Appearance ??= new AppearanceOptions();
        GitLabAccounts ??= [];
        foreach (var a in GitLabAccounts)
        {
            a.Host = (a.Host ?? "").ToLowerInvariant();
            a.Scopes ??= [];
            a.WebBaseUrl ??= "";
            a.ClientId ??= "";
        }
        GitLabAccounts.RemoveAll(a => a.Host.Length == 0);
        GitLabDismissedHosts ??= [];
        RepoHostingRemote ??= new();
        General.AutoFetchIntervalMinutes = Math.Clamp(General.AutoFetchIntervalMinutes, 1, 120);
        // A hand-edited (or older) settings file must not be able to leave the window at a
        // scale nothing can be clicked at, or the diff at a size with no glyphs in it.
        Appearance.CodeFont ??= "";
        Appearance.UiScale = Appearance.UiScale <= 0f ? 1f : Math.Clamp(Appearance.UiScale, MinUiScale, MaxUiScale);
        Appearance.CodeFontSize = Appearance.CodeFontSize <= 0f
            ? 12f
            : Math.Clamp(Appearance.CodeFontSize, MinCodeFontSize, MaxCodeFontSize);

        foreach (var ws in Workspaces)
            ws.Normalize();

        // Migrate a pre-workspaces config: fold every known repo into one
        // default workspace so the sidebar has something to show.
        if (Workspaces.Count == 0 && RepositoryPaths.Count > 0)
        {
            Workspaces.Add(new WorkspaceData
            {
                Id = "default",
                Name = "My repositories",
                Icon = "stack",
                RepoPaths = [.. RepositoryPaths],
            });
        }

        // Keep the flat RepositoryPaths in sync as the union of all workspaces.
        var union = Workspaces.SelectMany(w => w.RepoPaths).Distinct().ToList();
        foreach (var p in union)
            if (!RepositoryPaths.Contains(p))
                RepositoryPaths.Add(p);

        // Clamp the active-workspace / active-repo pointers to something valid.
        if (Workspaces.Count > 0)
        {
            var active = Workspaces.FirstOrDefault(w => w.Id == ActiveWorkspaceId) ?? Workspaces[0];
            ActiveWorkspaceId = active.Id;
            if (ActiveRepoPath == null || !active.RepoPaths.Contains(ActiveRepoPath))
                ActiveRepoPath = active.RepoPaths.FirstOrDefault();
        }
    }

    /// <summary>Bounds on the interface zoom and the code size, shared by the loader's clamp
    /// and the picker that offers the steps.</summary>
    public const float MinUiScale = 0.75f;
    public const float MaxUiScale = 2f;
    public const float MinCodeFontSize = 9f;
    public const float MaxCodeFontSize = 24f;

    public JunctionSettingsData Clone() => new()
    {
        RepositoryPaths = [.. RepositoryPaths],
        ActiveRepositoryIndex = ActiveRepositoryIndex,
        Workspaces = Workspaces.Select(w => w.Clone()).ToList(),
        ActiveWorkspaceId = ActiveWorkspaceId,
        ActiveRepoPath = ActiveRepoPath,
        DiffWrap = DiffWrap,
        DiffSplit = DiffSplit,
        RefTreeView = RefTreeView,
        CommitMessageExpanded = CommitMessageExpanded,
        CommitFileListWidth = CommitFileListWidth,
        ChangesFileListWidth = ChangesFileListWidth,
        RepoBadges = new(RepoBadges),
        LastBranchRoot = LastBranchRoot,
        General = General.Clone(),
        Git = Git.Clone(),
        ExternalTools = ExternalTools.Clone(),
        Appearance = Appearance.Clone(),
    };
}

/// <summary>A named group of repositories opened together.</summary>
public sealed class WorkspaceData
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>A logical icon key (e.g. "stack", "browsers", "shapes"); the UI maps it to a glyph.</summary>
    public string Icon { get; set; } = "stack";
    /// <summary>The workspace's icon-colour swatch id (see Swatches).</summary>
    public string Color { get; set; } = "blurple";
    public List<string> RepoPaths { get; set; } = [];

    public void Normalize()
    {
        RepoPaths ??= [];
        if (string.IsNullOrWhiteSpace(Id))
            Id = Guid.NewGuid().ToString("N")[..8];
        if (string.IsNullOrWhiteSpace(Icon))
            Icon = "stack";
        if (string.IsNullOrWhiteSpace(Color))
            Color = "blurple";
    }

    public WorkspaceData Clone() => new()
    {
        Id = Id,
        Name = Name,
        Icon = Icon,
        Color = Color,
        RepoPaths = [.. RepoPaths],
    };
}

public sealed class GeneralOptions
{
    /// <summary>Whether Junction fetches in the background so ahead/behind counts and
    /// remote branches are current without anyone pressing Fetch. Off until asked for —
    /// this reaches the network on a timer, which is not something to start doing to
    /// someone's machine because they upgraded. Turned on from the command palette.</summary>
    public bool AutoFetchEnabled { get; set; }
    public int AutoFetchIntervalMinutes { get; set; } = 5;

    /// <summary>All open repositories by default: the sidebar shows an ahead/behind count
    /// for every repository in the workspace, and a count is only worth showing if it is
    /// current. Fetching one of them would leave the rest quietly lying.</summary>
    public AutoFetchMode AutoFetchMode { get; set; } = AutoFetchMode.AllOpenRepositories;

    /// <summary>Minutes between reloads of the hosted (GitLab) data for the active
    /// repository; 0 reloads only on Fetch, on opening the panel and on a repository switch.
    /// Every hosted surface shows its as-of time and marks itself stale after ten minutes
    /// either way.</summary>
    public int GitLabRefreshMinutes { get; set; }

    /// <summary>Ask before discarding working-tree changes or deleting a branch that is
    /// fully merged. A branch with unmerged work always asks: that card is a choice, not
    /// a confirmation.</summary>
    public bool ConfirmDestructive { get; set; } = true;

    /// <summary>GitLab todo kinds (<c>action_name</c> values) that do not ring the bell.</summary>
    public List<string> TodoHiddenKinds { get; set; } = [];

    /// <summary>Only todos on projects that a repository in the open workspace is a clone
    /// of ring the bell; the rest stay on GitLab.</summary>
    public bool TodoOnlyWorkspaceRepos { get; set; }

    public GeneralOptions Clone() => new()
    {
        AutoFetchEnabled = AutoFetchEnabled,
        AutoFetchIntervalMinutes = AutoFetchIntervalMinutes,
        AutoFetchMode = AutoFetchMode,
        GitLabRefreshMinutes = GitLabRefreshMinutes,
        ConfirmDestructive = ConfirmDestructive,
        TodoHiddenKinds = [.. TodoHiddenKinds],
        TodoOnlyWorkspaceRepos = TodoOnlyWorkspaceRepos,
    };
}

public enum AutoFetchMode
{
    CurrentRepository,
    AllOpenRepositories,
}

public sealed class GitOptions
{
    public PullStrategy PullStrategy { get; set; } = PullStrategy.Rebase;
    public bool PullAutoStash { get; set; } = true;
    public bool PruneOnFetch { get; set; } = true;
    /// <summary>Default for the composer's Sign toggle: GPG/SSH-sign new commits
    /// (<c>git commit -S</c>). Requires a signing key configured in git.</summary>
    public bool SignCommits { get; set; }

    /// <summary>Diff viewer: pass <c>--ignore-all-space</c> so whitespace-only
    /// changes are hidden. Display-only — interactive staging is disabled while on.</summary>
    public bool IgnoreWhitespace { get; set; }

    public GitOptions Clone() => new()
    {
        PullStrategy = PullStrategy,
        PullAutoStash = PullAutoStash,
        PruneOnFetch = PruneOnFetch,
        SignCommits = SignCommits,
        IgnoreWhitespace = IgnoreWhitespace,
    };
}

public enum PullStrategy
{
    Merge,
    Rebase,
    FastForwardOnly,
}

public sealed class ExternalToolOptions
{
    public ExternalToolPreset DiffToolPreset { get; set; } = ExternalToolPreset.None;
    public string ExternalDiffToolPath { get; set; } = "";
    public string ExternalDiffToolArgs { get; set; } = "";
    public string ExternalMergeToolPath { get; set; } = "";
    public string ExternalMergeToolArgs { get; set; } = "";

    /// <summary>The editor Ctrl+D opens a diff in. <see cref="EditorPreset.Custom"/> runs
    /// <see cref="EditorCommand"/>, with <c>{left}</c> and <c>{right}</c> for the two files.</summary>
    public EditorPreset Editor { get; set; } = EditorPreset.VSCode;
    public string EditorCommand { get; set; } = "";

    /// <summary>What "Open in terminal" starts. System is what the machine opens on its own:
    /// Windows Terminal when it is there, the configured interpreter otherwise.</summary>
    public TerminalPreset Terminal { get; set; } = TerminalPreset.System;

    /// <summary>The three-way tool the conflicts view can hand a file to. None hides the
    /// button; Custom runs <see cref="ExternalMergeToolPath"/> with
    /// <see cref="ExternalMergeToolArgs"/> (<c>{base} {local} {remote} {merged}</c>).</summary>
    public MergeToolPreset MergeTool { get; set; } = MergeToolPreset.None;

    /// <summary>A command of the reader's own, offered on file rows: <c>$file</c>, <c>$line</c>
    /// and <c>$repo</c> are filled in. Empty hides the entry.</summary>
    public string CustomCommand { get; set; } = "";

    public ExternalToolOptions Clone() => new()
    {
        DiffToolPreset = DiffToolPreset,
        ExternalDiffToolPath = ExternalDiffToolPath,
        ExternalDiffToolArgs = ExternalDiffToolArgs,
        ExternalMergeToolPath = ExternalMergeToolPath,
        ExternalMergeToolArgs = ExternalMergeToolArgs,
        Editor = Editor,
        EditorCommand = EditorCommand,
        Terminal = Terminal,
        MergeTool = MergeTool,
        CustomCommand = CustomCommand,
    };
}

public enum MergeToolPreset
{
    None,
    VSCode,
    BeyondCompare,
    Meld,
    KDiff3,
    Custom,
}

public enum EditorPreset
{
    VSCode,
    VisualStudio,
    Rider,
    Custom,
}

public enum TerminalPreset
{
    System,
    WindowsTerminal,
    PowerShell,
    CommandPrompt,
}

public enum ExternalToolPreset
{
    None,
    VSCode,
    VisualStudio,
    Rider,
    BeyondCompare,
    Meld,
    KDiff3,
    Custom,
}

public sealed class AppearanceOptions
{
    /// <summary>Which colour scheme, by <c>NamedTheme.Id</c>. An id this build does not know
    /// reads back as the default rather than as an error, so a settings file that has been
    /// round-tripped through a newer build still opens.</summary>
    public string Theme { get; set; } = "nocturne";

    /// <summary>The user's zoom on top of whatever the OS asks for: 1.0 is "as the system
    /// says", 1.25 draws the whole interface a quarter larger. A multiplier on the display
    /// scale rather than a font size, because everything here is laid out in fixed units —
    /// growing the text alone would leave the rows, gutters and icons around it behind.</summary>
    public float UiScale { get; set; } = 1f;

    /// <summary>The family the diff, the file viewer and the blame body are drawn in. Empty
    /// means the platform's default monospace, which is what most people want and what every
    /// earlier build used. A name no font on the machine answers to falls back the same way
    /// any missing family does, so a settings file moved between machines still opens.</summary>
    public string CodeFont { get; set; } = "";

    /// <summary>Point size for that same code text. The row height of the diff follows it,
    /// so the lines do not crowd as it grows.</summary>
    public float CodeFontSize { get; set; } = 12f;

    /// <summary>How many columns a tab advances to in the diff. The text is expanded for
    /// drawing and measuring only; a copy still carries the tabs.</summary>
    public int TabWidth { get; set; } = 4;

    public AppearanceOptions Clone() => new()
    {
        Theme = Theme,
        UiScale = UiScale,
        CodeFont = CodeFont,
        CodeFontSize = CodeFontSize,
        TabWidth = TabWidth,
    };
}

public static class JunctionSettings
{
    /// <summary>
    /// Reads the stored settings, falling back to the backup <see cref="Save"/> keeps when the
    /// live file cannot be parsed, and to defaults when neither can (see <see cref="JsonFile{T}"/>).
    /// <para>The fallback matters because losing this file is silent: an unreadable config
    /// looks exactly like a first run, so the repository list simply comes back empty.</para>
    /// </summary>
    public static JunctionSettingsData Load() => Normalized(File.Load()) ?? new JunctionSettingsData();

    /// <summary>Reads a settings file at an explicit path — a harness borrowing the user's
    /// accounts from the real file while running against a scratch config, for one.</summary>
    public static JunctionSettingsData? LoadFrom(string path) =>
        Normalized(new JsonFile<JunctionSettingsData>(path, JunctionSettingsJsonContext.Default.JunctionSettingsData).Load());

    /// <summary>Writes the settings, keeping the previous contents as <c>settings.json.bak</c>.
    /// Best-effort: the UI keeps working when the config cannot be written.</summary>
    public static void Save(JunctionSettingsData settings) => File.Save(settings);

    private static JunctionSettingsData? Normalized(JunctionSettingsData? settings)
    {
        settings?.Normalize();
        return settings;
    }

    // Resolved on every use, as it always was: tests point XDG_CONFIG_HOME at a fresh folder
    // per test, and a path kept from the first one would write into the wrong one.
    private static JsonFile<JunctionSettingsData> File =>
        new(AppFolders.Config("HultGit"), "settings.json", JunctionSettingsJsonContext.Default.JunctionSettingsData);
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(JunctionSettingsData))]
[JsonSerializable(typeof(WorkspaceData))]
internal partial class JunctionSettingsJsonContext : JsonSerializerContext;
