using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Events;
using HultUI.Headless;
using Meridian.Tests.Fixtures;
using Xunit;

namespace Meridian.Tests;

/// <summary>P11: the command palette, the options surface, and one pass over
/// every view's accessibility tree.</summary>
public class PaletteTests : IClassFixture<TestHttpServer>, IDisposable
{
    private readonly TestHttpServer _server;
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-palette-").FullName;

    public PaletteTests(TestHttpServer server)
    {
        _server = server;
        Write("charges/get-charge.req", $"GET {_server.Origin}/ok\nname Get charge\n");
        Write("envs/local.env", "unused=1\n");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }


    private void Hosted(Action<HeadlessSession, MeridianApp> body, bool open = true)
    {
        using var session = HeadlessSession.Create(1600, 900);
        MeridianApp? app = null;
        try
        {
            app = new MeridianApp(session.Application, session.Window.Window);
            session.Application.SetRoot(app.Mount());
            if (open) app.OpenWorkspace(_root);
            session.PumpFrame();
            body(session, app);
        }
        finally
        {
            app?.Dispose();
        }
    }

    private static bool Contains(HeadlessSession session, string text) =>
        Walk(session.Window.AccessibilityTree).Any(n =>
            n.Name?.Contains(text, StringComparison.Ordinal) == true
            || n.Value?.Contains(text, StringComparison.Ordinal) == true);

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Walk(child))
                yield return descendant;
    }

    private static void Click(HeadlessSession session, string automationId)
    {
        var el = session.Window.FindByAutomationId(automationId);
        Assert.True(el != null, $"#{automationId} not found");
        el!.Click();
        session.PumpFrames(2);
    }

    private static void Press(HeadlessSession session, KeyCode key, KeyModifiers mods = KeyModifiers.None)
    {
        session.Window.PressKey(key, mods);
        session.PumpFrames(2);
    }

    // ── Matching ─────────────────────────────────────────────────────────

    [Theory]
    // Not "gtr": that is an honest tie — Go to *R*equests matches it just as
    // well — so the case has to be one only the intended command satisfies.
    [InlineData("grun", "Go to Runner")]
    [InlineData("runner", "Go to Runner")]
    [InlineData("environments", "Go to Environments")]
    public void SubsequenceMatchingFindsCommands(string query, string expected)
    {
        var commands = new List<PaletteCommand>
        {
            new PaletteCommand("Go to Requests", () => { }) { Group = "Go", Keys = "1" },
            new PaletteCommand("Go to Runner", () => { }) { Group = "Go", Keys = "2" },
            new PaletteCommand("Go to Environments", () => { }) { Group = "Go", Keys = "5" },
        };

        Assert.Equal(expected, CommandPalette.Filter(commands, query).First().Title);
    }

    [Theory]
    [InlineData("zzz")]              // no letter matches
    [InlineData("runnerx")]          // all but the last letter matches
    [InlineData("rennur")]           // the right letters, the wrong order
    public void AQueryThatDoesNotMatchReturnsNothing(string query)
    {
        var commands = new List<PaletteCommand> { new PaletteCommand("Go to Runner", () => { }) { Group = "Go" } };
        Assert.Empty(CommandPalette.Filter(commands, query));
    }

    [Fact]
    public void TighterMatchesRankFirst()
    {
        var commands = new List<PaletteCommand>
        {
            new PaletteCommand("Save the whole environment set", () => { }) { Group = "Environment" },
            new PaletteCommand("Save", () => { }) { Group = "Request" },
        };

        Assert.Equal("Save", CommandPalette.Filter(commands, "save").First().Title);
    }

    // ── The palette ──────────────────────────────────────────────────────

    [Fact]
    public void CtrlPOpensAndEscCloses()
    {
        Hosted((session, app) =>
        {
            Press(session, KeyCode.P, KeyModifiers.Ctrl);
            Assert.True(app.PaletteOpen);
            Assert.NotNull(session.Window.FindByAutomationId("palette-input"));

            Press(session, KeyCode.Escape);
            Assert.False(app.PaletteOpen);
        });
    }

    [Fact]
    public void TypingFiltersAndEnterRuns()
    {
        Hosted((session, app) =>
        {
            Press(session, KeyCode.P, KeyModifiers.Ctrl);
            session.Window.TypeText("runner");
            session.PumpFrames(3);
            Press(session, KeyCode.Enter);

            Assert.False(app.PaletteOpen);
            Assert.Equal(MeridianView.Runner, app.View);
        });
    }

    [Fact]
    public void ArrowsMoveTheSelection()
    {
        Hosted((session, app) =>
        {
            // Ask the same filter the palette uses which command sits third,
            // rather than guessing a ranking — the assertion is "two Downs
            // select index 2", not "index 2 happens to be view X".
            var third = CommandPalette.Filter(app.Commands(), "go to")[2].Title;
            Assert.StartsWith("Go to ", third);

            Press(session, KeyCode.P, KeyModifiers.Ctrl);
            session.Window.TypeText("go to");
            session.PumpFrames(3);
            Press(session, KeyCode.Down);
            Press(session, KeyCode.Down);
            Press(session, KeyCode.Enter);

            Assert.Equal(third["Go to ".Length..], app.View.ToString());
        });
    }

    [Fact]
    public void EveryRequestIsACommand()
    {
        Hosted((session, app) =>
        {
            Press(session, KeyCode.P, KeyModifiers.Ctrl);
            session.Window.TypeText("get charge");
            session.PumpFrames(3);
            Press(session, KeyCode.Enter);

            Assert.Equal(MeridianView.Requests, app.View);
            Assert.EndsWith("get-charge.req", app.ActiveTab!.Path);
        });
    }

    [Fact]
    public void CommandsForUnavailableFeaturesAreNotListed()
    {
        // No collection open: nothing collection-shaped may appear, because a
        // palette entry that cannot work is the same phantom as a dead button.
        Hosted((session, app) =>
        {
            var titles = app.Commands().Select(c => c.Title).ToList();

            Assert.DoesNotContain("Run the collection", titles);
            Assert.DoesNotContain("Switch environment…", titles);
            Assert.DoesNotContain(titles, t => t.StartsWith("Send", StringComparison.Ordinal));
            Assert.Contains("Open a folder…", titles);
        }, open: false);
    }

    [Fact]
    public void AnEmptyResultSaysSo()
    {
        Hosted((session, app) =>
        {
            Press(session, KeyCode.P, KeyModifiers.Ctrl);
            session.Window.TypeText("zzzz");
            session.PumpFrames(3);

            Assert.NotNull(session.Window.FindByAutomationId("palette-no-match"));
        });
    }

    // ── Options ──────────────────────────────────────────────────────────

    [Fact]
    public void OptionsOpenFromThePaletteAndToggleRealState()
    {
        Hosted((session, app) =>
        {
            Press(session, KeyCode.P, KeyModifiers.Ctrl);
            session.Window.TypeText("options");
            session.PumpFrames(3);
            Press(session, KeyCode.Enter);

            Assert.True(app.OptionsOpen);
            Assert.False(app.ResponseAtBottom);

            Click(session, "option-response-bottom");
            Assert.True(app.ResponseAtBottom);   // the setting is the real state

            Press(session, KeyCode.Escape);
            Assert.False(app.OptionsOpen);
        });
    }

    [Fact]
    public void ALockedOptionSaysWhyRatherThanLying()
    {
        // Below the breakpoint the pane cannot move, so the row reports it
        // instead of offering a toggle that would do nothing.
        using var session = HeadlessSession.Create(1000, 900);
        var app = new MeridianApp(session.Application, session.Window.Window);
        try
        {
            session.Application.SetRoot(app.Mount());
            app.OpenWorkspace(_root);
            session.PumpFrame();
            Press(session, KeyCode.P, KeyModifiers.Ctrl);
            session.Window.TypeText("options");
            session.PumpFrames(3);
            Press(session, KeyCode.Enter);

            Assert.True(Contains(session, "forced — the window is too narrow"));
        }
        finally
        {
            app.Dispose();
        }
    }

    // ── Accessibility pass ───────────────────────────────────────────────

    [Fact]
    public void EveryViewExposesNamedControls()
    {
        // The a11y pass: each view must put *something* named in the tree, and
        // no control may reach it without a name — an unnamed button is
        // unreachable by a screen reader and unfindable by any future test.
        Hosted((session, app) =>
        {
            foreach (var (key, view) in new[]
            {
                (KeyCode.Digit1, MeridianView.Requests),
                (KeyCode.Digit2, MeridianView.Runner),
                (KeyCode.Digit3, MeridianView.Discovery),
                (KeyCode.Digit4, MeridianView.History),
                (KeyCode.Digit5, MeridianView.Environments),
            })
            {
                Press(session, key);
                Assert.Equal(view, app.View);

                var buttons = Walk(session.Window.AccessibilityTree)
                    .Where(n => n.Role is AccessibilityRole.Button or AccessibilityRole.Checkbox
                        or AccessibilityRole.RadioButton)
                    .ToList();

                Assert.True(buttons.Count > 0, $"{view} exposes no controls at all");
                Assert.All(buttons, b =>
                    Assert.False(string.IsNullOrWhiteSpace(b.Name),
                        $"an unnamed control in {view}"));
            }
        });
    }

    [Fact]
    public void TheOverlaysAreDialogsWithNames()
    {
        Hosted((session, app) =>
        {
            Press(session, KeyCode.P, KeyModifiers.Ctrl);
            var palette = Walk(session.Window.AccessibilityTree)
                .FirstOrDefault(n => n.Role == AccessibilityRole.Dialog);

            Assert.True(palette != null, "the palette should be a named dialog");
            Assert.Equal("Command palette", palette!.Name);
        });
    }

    // ── keys the hints name ──────────────────────────────────────────────
    // The hints were typed by hand, and three named keys that did nothing. They read the same
    // table the key handler does now.

    [Fact]
    public void CtrlI_OpensTheImportDialogThePaletteAndTheFrontDoorOffer()
    {
        Hosted((session, app) =>
        {
            Assert.False(Contains(session, "Import OpenAPI"));
            session.Window.PressKey(KeyCode.I, KeyModifiers.Ctrl);
            session.PumpFrame();
            Assert.True(Contains(session, "Import OpenAPI"), "Ctrl+I did nothing");
        });
    }

    [Fact]
    public void CtrlR_RunsTheCollectionFromAnyView_AsThePaletteSays()
    {
        Hosted((session, app) =>
        {
            app.View = MeridianView.Requests;
            session.PumpFrame();
            session.Window.PressKey(KeyCode.R, KeyModifiers.Ctrl);
            session.PumpFrame();
            Assert.Equal(MeridianView.Runner, app.View);
            session.PumpUntil(() => !app.RunnerRunning, TimeSpan.FromSeconds(15));
        });
    }

    [Fact]
    public void TheCardForWhatIsNotBuilt_NamesNoKey()
    {
        Hosted((session, app) =>
        {
            Assert.True(Contains(session, "New collection"));
            Assert.False(Contains(session, "Ctrl+N") || Contains(session, "Ctrl N"),
                "the inert New collection card still advertises a key");
            Assert.True(Contains(session, "Ctrl+I"));   // the import card's key, which works now
        }, open: false);
    }
}
