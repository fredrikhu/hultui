using System.Text.Json;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The settings model. <c>Normalize</c> runs on every load, so it is the one piece of
/// code that meets configs written by older builds — and the one place a wrong default
/// can hand the UI a null list or an active pointer aimed at a repository that is not
/// in the open workspace.
///
/// <para><c>Load</c> and <c>Save</c> are covered too, which they could not be while they
/// read and wrote the developer's real config — a test that clobbered it would have been a
/// bug of its own, and for a while the hosted app tests were exactly that bug. They are
/// testable now because <see cref="SettingsIsolation"/> redirects the config directory for
/// the whole process; each test below still takes its own directory so it can assert on the
/// files without depending on what another test left behind.</para>
/// </summary>
public class SettingsTests
{
    [Fact]
    public void APreWorkspacesConfigIsFoldedIntoOneDefaultWorkspace()
    {
        // Configs written before workspaces existed carry a flat repository list. Left
        // alone they produce an empty sidebar, which reads as "Junction lost my repos".
        var s = new JunctionSettingsData { RepositoryPaths = ["/a", "/b"] };

        s.Normalize();

        var ws = Assert.Single(s.Workspaces);
        Assert.Equal("default", ws.Id);
        Assert.Equal(new[] { "/a", "/b" }, ws.RepoPaths);
        Assert.Equal("default", s.ActiveWorkspaceId);
        Assert.Equal("/a", s.ActiveRepoPath);
    }

    [Fact]
    public void AnEmptyConfigStaysEmptyRatherThanInventingAWorkspace()
    {
        var s = new JunctionSettingsData();

        s.Normalize();

        Assert.Empty(s.Workspaces);
        Assert.Null(s.ActiveWorkspaceId);
    }

    [Fact]
    public void TheFlatRepositoryListBecomesTheUnionOfEveryWorkspace()
    {
        // RepositoryPaths is the "every repo Junction knows" set that the add-repo
        // picker and the recent list read from.
        var s = new JunctionSettingsData
        {
            RepositoryPaths = ["/a"],
            Workspaces =
            [
                new WorkspaceData { Id = "w1", RepoPaths = ["/a", "/b"] },
                new WorkspaceData { Id = "w2", RepoPaths = ["/c"] },
            ],
        };

        s.Normalize();

        Assert.Equal(new[] { "/a", "/b", "/c" }, s.RepositoryPaths);
    }

    [Fact]
    public void AnActiveWorkspaceIdThatNoLongerExistsFallsBackToTheFirst()
    {
        var s = new JunctionSettingsData
        {
            ActiveWorkspaceId = "deleted",
            Workspaces = [new WorkspaceData { Id = "w1", RepoPaths = ["/a"] }],
        };

        s.Normalize();

        Assert.Equal("w1", s.ActiveWorkspaceId);
    }

    [Fact]
    public void AnActiveRepoOutsideTheActiveWorkspaceIsPulledBackIn()
    {
        // Otherwise the toolbar names a repository the sidebar is not showing, and
        // every action runs against a tree the user cannot see.
        var s = new JunctionSettingsData
        {
            ActiveWorkspaceId = "w1",
            ActiveRepoPath = "/elsewhere",
            Workspaces = [new WorkspaceData { Id = "w1", RepoPaths = ["/a", "/b"] }],
        };

        s.Normalize();

        Assert.Equal("/a", s.ActiveRepoPath);
    }

    [Fact]
    public void AnActiveRepoInsideTheActiveWorkspaceIsLeftAlone()
    {
        var s = new JunctionSettingsData
        {
            ActiveWorkspaceId = "w1",
            ActiveRepoPath = "/b",
            Workspaces = [new WorkspaceData { Id = "w1", RepoPaths = ["/a", "/b"] }],
        };

        s.Normalize();

        Assert.Equal("/b", s.ActiveRepoPath);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(5, 5)]
    [InlineData(500, 120)]
    public void TheAutoFetchIntervalIsClampedToSomethingSane(int stored, int expected)
    {
        // A hand-edited 0 would schedule a fetch loop with no delay.
        var s = new JunctionSettingsData();
        s.General.AutoFetchIntervalMinutes = stored;

        s.Normalize();

        Assert.Equal(expected, s.General.AutoFetchIntervalMinutes);
    }

    [Fact]
    public void AWorkspaceWithNoIdGetsOneSoItCanBeSelected()
    {
        // Everything downstream keys off the id — an empty one makes two workspaces
        // indistinguishable.
        var s = new JunctionSettingsData { Workspaces = [new WorkspaceData { Name = "Work" }] };

        s.Normalize();

        var ws = Assert.Single(s.Workspaces);
        Assert.NotEqual("", ws.Id);
        Assert.Equal("stack", ws.Icon);
        Assert.Equal("blurple", ws.Color);
    }

    [Fact]
    public void CloneIsDeepEnoughToBackAnOptionsDialog()
    {
        // The options dialog edits a clone and discards it on Cancel. A shared list
        // would make Cancel a no-op.
        var s = new JunctionSettingsData
        {
            RepositoryPaths = ["/a"],
            Workspaces = [new WorkspaceData { Id = "w1", RepoPaths = ["/a"] }],
            RepoBadges = { ["/a"] = "amber" },
        };

        var draft = s.Clone();
        draft.RepositoryPaths.Add("/added");
        draft.Workspaces[0].RepoPaths.Add("/added");
        draft.Workspaces[0].Name = "renamed";
        draft.RepoBadges["/a"] = "teal";
        draft.General.AutoFetchEnabled = !s.General.AutoFetchEnabled;

        Assert.Equal(new[] { "/a" }, s.RepositoryPaths);
        Assert.Equal(new[] { "/a" }, s.Workspaces[0].RepoPaths);
        Assert.NotEqual("renamed", s.Workspaces[0].Name);
        Assert.Equal("amber", s.RepoBadges["/a"]);
        Assert.NotEqual(draft.General.AutoFetchEnabled, s.General.AutoFetchEnabled);
    }

    [Fact]
    public void TheSourceGeneratedContextRoundTripsEverySection()
    {
        // Junction publishes Native AOT, so serialization goes through the generated
        // context rather than reflection. A section missing a [JsonSerializable] entry
        // does not fail to build — it silently round-trips as its default.
        var s = new JunctionSettingsData
        {
            RepositoryPaths = ["/a"],
            ActiveWorkspaceId = "w1",
            ActiveRepoPath = "/a",
            Workspaces = [new WorkspaceData { Id = "w1", Name = "Work", Icon = "browsers", Color = "amber", RepoPaths = ["/a"] }],
            RepoBadges = { ["/a"] = "teal" },
        };
        s.General.AutoFetchEnabled = true;
        s.General.AutoFetchIntervalMinutes = 17;

        var json = JsonSerializer.Serialize(s, JunctionSettingsJsonContext.Default.JunctionSettingsData);
        var back = JsonSerializer.Deserialize(json, JunctionSettingsJsonContext.Default.JunctionSettingsData)!;

        Assert.Equal(new[] { "/a" }, back.RepositoryPaths);
        Assert.Equal("w1", back.ActiveWorkspaceId);
        Assert.Equal("/a", back.ActiveRepoPath);
        Assert.Equal("teal", back.RepoBadges["/a"]);
        Assert.True(back.General.AutoFetchEnabled);
        Assert.Equal(17, back.General.AutoFetchIntervalMinutes);

        var ws = Assert.Single(back.Workspaces);
        Assert.Equal("Work", ws.Name);
        Assert.Equal("browsers", ws.Icon);
        Assert.Equal("amber", ws.Color);
        Assert.Equal(new[] { "/a" }, ws.RepoPaths);
    }

    // ── Load / Save against the file ─────────────────────────────────────

    [Fact]
    public void SavingThenLoadingReturnsWhatWasStored()
    {
        InIsolatedConfig(_ =>
        {
            JunctionSettings.Save(WithRepo("/a"));

            Assert.Equal(new[] { "/a" }, JunctionSettings.Load().RepositoryPaths);
        });
    }

    /// <summary>The config directory does not exist before the first save — the very case
    /// where there is no file to replace, so the write has to take the other branch.</summary>
    [Fact]
    public void TheFirstSaveCreatesTheConfigDirectory()
    {
        InIsolatedConfig(dir =>
        {
            Assert.False(Directory.Exists(dir), "the fixture handed the test an existing config dir");

            JunctionSettings.Save(WithRepo("/a"));

            Assert.True(File.Exists(Path.Combine(dir, "settings.json")));
            Assert.Equal(new[] { "/a" }, JunctionSettings.Load().RepositoryPaths);
        });
    }

    [Fact]
    public void AnOverwriteKeepsThePreviousContentsAsABackup()
    {
        InIsolatedConfig(dir =>
        {
            JunctionSettings.Save(WithRepo("/first"));
            JunctionSettings.Save(WithRepo("/second"));

            Assert.Equal(new[] { "/second" }, JunctionSettings.Load().RepositoryPaths);
            Assert.Equal(new[] { "/first" }, ReadJson(Path.Combine(dir, "settings.json.bak")).RepositoryPaths);
        });
    }

    /// <summary>A config that cannot be parsed is indistinguishable from a first run, so
    /// without the fallback a single bad write silently empties the sidebar.</summary>
    [Fact]
    public void ACorruptConfigIsReadFromTheBackupInstead()
    {
        InIsolatedConfig(dir =>
        {
            JunctionSettings.Save(WithRepo("/first"));
            JunctionSettings.Save(WithRepo("/second"));
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{ this is not json");

            Assert.Equal(new[] { "/first" }, JunctionSettings.Load().RepositoryPaths);
        });
    }

    [Fact]
    public void AConfigAndBackupThatAreBothUnreadableFallBackToDefaults()
    {
        InIsolatedConfig(dir =>
        {
            JunctionSettings.Save(WithRepo("/first"));
            JunctionSettings.Save(WithRepo("/second"));
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{ this is not json");
            File.WriteAllText(Path.Combine(dir, "settings.json.bak"), "nor is this");

            Assert.Empty(JunctionSettings.Load().RepositoryPaths);
        });
    }

    /// <summary>The scratch file the swap is staged through belongs to the save, not to the
    /// config directory — one left behind would be read as a stray config by anything
    /// globbing the folder, and hides that a write failed.</summary>
    [Fact]
    public void TheStagingFileDoesNotSurviveTheSave()
    {
        InIsolatedConfig(dir =>
        {
            JunctionSettings.Save(WithRepo("/a"));
            JunctionSettings.Save(WithRepo("/b"));

            Assert.False(File.Exists(Path.Combine(dir, "settings.json.tmp")));
        });
    }

    /// <summary>The redirect itself, which is what keeps the hosted app tests off the
    /// developer's real config. Deliberately does not take a directory of its own: the claim
    /// is about the process-wide isolation <see cref="SettingsIsolation"/> installs, so a
    /// test that set the variable itself would prove nothing.</summary>
    [Fact]
    public void TheProcessWideRedirectSendsSavesToTheThrowawayConfig()
    {
        var written = Path.Combine(SettingsIsolation.ConfigHome, "HultGit", "settings.json");

        // Cleared first, and asserted gone. Reading the file straight after the save would
        // pass on one left by an earlier run even with the redirect removed — the guard has
        // to show that *this* save arrived, not that the directory has ever been written to.
        try { File.Delete(written); } catch { /* asserted below */ }
        Assert.False(File.Exists(written), "the probe file could not be cleared");

        JunctionSettings.Save(WithRepo("/probe"));

        Assert.True(File.Exists(written),
            "the save went somewhere other than the redirected config directory — " +
            "SettingsIsolation is not in effect, and the hosted tests are writing the real settings.json");
        Assert.Equal(new[] { "/probe" }, ReadJson(written).RepositoryPaths);
    }

    // ── harness ──────────────────────────────────────────────────────────

    private static JunctionSettingsData WithRepo(string path) =>
        new()
        {
            RepositoryPaths = [path],
            ActiveRepoPath = path,
            Workspaces = [new WorkspaceData { Id = "w1", Name = "W", RepoPaths = [path] }],
            ActiveWorkspaceId = "w1",
        };

    private static JunctionSettingsData ReadJson(string path) =>
        JsonSerializer.Deserialize(File.ReadAllText(path),
            JunctionSettingsJsonContext.Default.JunctionSettingsData)!;

    /// <summary>Runs the body with the config redirected to a directory of its own, and
    /// hands it the path Junction will use. Restores the process-wide redirect afterwards —
    /// the suite runs unparallelized, so borrowing the variable for one test is safe.</summary>
    private static void InIsolatedConfig(Action<string> body)
    {
        var previous = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var home = Path.Combine(Path.GetTempPath(), "junction-settings-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", home);
        try
        {
            body(Path.Combine(home, "HultGit"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previous);
            try { Directory.Delete(home, recursive: true); } catch { /* best-effort temp cleanup */ }
        }
    }
}
