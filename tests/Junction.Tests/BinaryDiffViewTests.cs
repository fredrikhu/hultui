using System.Reflection;
using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The binary summary as the app actually shows it. Assertions go through the
/// accessibility tree, which is what a screen reader walks: a card that cannot be found
/// here is a card nobody can read either.
///
/// <para>The old symptom is asserted alongside the new behaviour — "No textual changes"
/// is what the pane used to say about a file that had changed, and a test that only
/// looked for the new text would still pass if both appeared.</para>
/// </summary>
[Collection("JunctionDiff")]
public class BinaryDiffViewTests
{
    private static byte[] Blob(int length, byte seed) =>
        Enumerable.Range(0, length).Select(i => (byte)((i * 7 + seed) % 251)).ToArray();

    [Fact]
    public void TheChangesViewSummarisesABinaryFileInsteadOfShowingAnEmptyDiff()
    {
        Hosted((session, app) =>
        {
            PumpUntilVisible(session, "Binary file");

            Assert.True(Contains(session, "Binary file · modified"),
                "the card does not name the change kind:\n" + session.Window.DumpAccessibilityTree());
            Assert.False(Contains(session, "No textual changes"),
                "the empty text-diff pane is still on screen");

            // The sizes are a second, asynchronous step (three git calls at worst), so the
            // card renders before they land and re-renders when they do.
            PumpUntilVisible(session, "600 B");
            Assert.True(Contains(session, "1.4 KB"), "the after size is missing");
            // The delta only appears once both sides are known, so it also says the two
            // numbers reached the same build.
            Assert.True(Contains(session, "+800 B"), "the size delta is missing");
        });
    }

    [Fact]
    public void TheSyntaxHighlighterIsNeverAskedToTokenizeABinaryFile()
    {
        // The mechanism rather than the pixels: tokenizing a blob is pure waste, and it
        // would be invisible on screen. Both caches must stay untouched — pending as well
        // as complete, since a request in flight is the expensive half.
        Hosted((session, app) =>
        {
            PumpUntilVisible(session, "Binary file");
            session.PumpFrames(4);

            Assert.Equal(0, CacheCount(app, "_highlights"));
            Assert.Equal(0, CacheCount(app, "_highlightsPending"));
        });
    }

    [Fact]
    public void TheFullscreenDiffSummarisesItToo()
    {
        Hosted((session, app) =>
        {
            PumpUntilVisible(session, "Binary file");

            // The affordance is an icon-only header button, so drive the state the button
            // sets. A rebuild has to be asked for: nothing else observes this field.
            Field("_diffFull").SetValue(app, true);
            Rebuild(app);
            session.PumpFrames(4);

            Assert.True(Contains(session, "Exit fullscreen (Esc)"), "fullscreen did not open");
            Assert.True(Contains(session, "Binary file · modified"),
                "fullscreen fell back to the text diff:\n" + session.Window.DumpAccessibilityTree());
            Assert.False(Contains(session, "No textual changes"));
        });
    }

    [Fact]
    public void OpeningABinaryDiffExternallyIsRefusedRatherThanAttempted()
    {
        // Ctrl+D does not go through the (disabled) header button, so the refusal has to
        // live in the action as well. VS Code would otherwise be handed two temp files of
        // mojibake — both sides are fetched as text.
        Hosted((session, app) =>
        {
            PumpUntilVisible(session, "Binary file");

            session.Window.PressKey(KeyCode.D, KeyModifiers.Ctrl);
            session.PumpFrames(3);

            Assert.True(Contains(session, "there are no two texts for VS Code to compare"),
                "no refusal was shown:\n" + session.Window.DumpAccessibilityTree());
        });
    }

    // ── harness ──────────────────────────────────────────────────────────

    /// <summary>Mounts the app over a repository whose only working change is a modified
    /// binary file, so the Changes view selects it without any driving.</summary>
    private static void Hosted(Action<HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.WriteBytes("assets/logo.pdf", Blob(600, 0));
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "add a binary asset");
        repo.WriteBytes("assets/logo.pdf", Blob(1400, 3));

        using var session = HeadlessSession.Create(1400, 900);
        JunctionApp? app = null;
        try
        {
            app = new JunctionApp(session.Application, session.Window.Window, new JunctionSettingsData
            {
                RepositoryPaths = [repo.Path],
                ActiveRepoPath = repo.Path,
                Workspaces = [new WorkspaceData { Id = "w1", Name = "Tests", RepoPaths = [repo.Path] }],
                ActiveWorkspaceId = "w1",
            });
            session.Application.SetRoot(app.Mount());
            session.PumpFrames(3);

            session.Window.PressKey(KeyCode.Digit4);   // Changes
            session.PumpFrames(3);

            body(session, app);
        }
        finally { app?.Dispose(); }
    }

    private static void PumpUntilVisible(HeadlessSession session, string text)
    {
        try
        {
            session.PumpUntil(() => Contains(session, text), TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException e)
        {
            throw new Xunit.Sdk.XunitException(
                $"\"{text}\" never appeared in the accessibility tree.\n{e.Message}\n" +
                session.Window.DumpAccessibilityTree());
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

    /// <summary>How many entries a private cache holds. Reflected rather than exposed:
    /// the caches are an implementation detail everywhere except here, where "it stayed
    /// empty" is the whole assertion.</summary>
    private static int CacheCount(JunctionApp app, string field)
    {
        var cache = Field(field).GetValue(app)!;
        return (int)cache.GetType().GetProperty("Count")!.GetValue(cache)!;
    }

    private static void Rebuild(JunctionApp app)
    {
        for (var t = typeof(JunctionApp); t != null; t = t.BaseType)
            if (t.GetMethod("MarkDirty", BindingFlags.NonPublic | BindingFlags.Instance) is { } m)
            { m.Invoke(app, null); return; }
        throw new Xunit.Sdk.XunitException("no MarkDirty to call");
    }

    private static FieldInfo Field(string name) =>
        typeof(JunctionApp).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new Xunit.Sdk.XunitException($"JunctionApp has no field {name}");
}
