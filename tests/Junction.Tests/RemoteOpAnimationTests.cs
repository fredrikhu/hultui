using System.Reflection;
using HultUI.Core.Animation;
using HultUI.Core.Elements;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The toolbar while a remote command is in flight: the button says what it is doing, and
/// its icon moves.
///
/// <para>A fetch is the one thing Junction does that can take seconds, and until now the
/// only sign of it was a toast that faded after 1.7 seconds — leaving a window that looked
/// idle while it was not. These pin the two halves of the fix: the label, which is what a
/// screen reader gets, and the motion, which is what everyone else does.</para>
///
/// <para>The animation is driven by a plain field and a rebuild, so the assertion is on
/// the built tree after ticking the clock rather than on anything the renderer did.</para>
/// </summary>
public class RemoteOpAnimationTests
{
    // The private enum the app uses for "which command is running".
    private static object RemoteOp(string name)
    {
        var type = typeof(JunctionApp).GetNestedType("RemoteOp", BindingFlags.NonPublic)!;
        return Enum.Parse(type, name);
    }

    private static void Invoke(JunctionApp app, string method, params object[] args) =>
        typeof(JunctionApp).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(app, args);

    private static IEnumerable<Element> Descendants(Element element)
    {
        yield return element;
        foreach (var child in element.Children)
            foreach (var d in Descendants(child))
                yield return d;
    }

    /// <summary>The toolbar button with the given key, whichever build it is on.</summary>
    private static Element Button(JunctionApp app, string keyPrefix) =>
        Descendants(app.Mount()).FirstOrDefault(e => e.Key?.StartsWith(keyPrefix, StringComparison.Ordinal) == true)
        ?? throw new Xunit.Sdk.XunitException($"no toolbar button keyed \"{keyPrefix}*\"");

    private static bool HasText(Element root, string text) =>
        Descendants(root).OfType<TextElement>().Any(t => t.Content == text);

    [Fact]
    public void AFetchInFlightSaysSoOnItsOwnButton()
    {
        Hosted((session, app) =>
        {
            Assert.True(HasText(Button(app, "fetch"), "Fetch"));

            Invoke(app, "BeginRemoteOp", RemoteOp("Fetch"));
            session.PumpFrame();

            var fetch = Button(app, "fetch");
            Assert.True(HasText(fetch, "Fetching…"),
                "the fetch button did not say it was fetching");
            // Keyed apart from the idle build: the busy one swaps the label and hands in a
            // different icon, so it has to reconcile as a different button.
            Assert.EndsWith("#busy", fetch.Key, StringComparison.Ordinal);

            Invoke(app, "EndRemoteOp");
            session.PumpFrame();
            Assert.True(HasText(Button(app, "fetch"), "Fetch"));
        });
    }

    [Fact]
    public void TheFetchIconTurnsWhileItRuns()
    {
        Hosted((session, app) =>
        {
            Invoke(app, "BeginRemoteOp", RemoteOp("Fetch"));
            session.PumpFrame();
            // A third of the way through a 1.1s turn.
            AnimationManager.Tick(0.4f);
            session.PumpFrame();

            var icon = Button(app, "fetch").Children[0];
            Assert.NotNull(icon.Transform);
            Assert.True(icon.Transform!.Value.RotationDegrees > 1f,
                $"the fetch icon had not turned (rotation {icon.Transform.Value.RotationDegrees}°)");

            Invoke(app, "EndRemoteOp");
        });
    }

    /// <summary>Pull travels down and push travels up — the way their own arrows point.
    /// Asserted as a sign rather than a distance: the amplitude is a design decision and
    /// the direction is the meaning.</summary>
    [Theory]
    [InlineData("Pull", "pull", 1)]
    [InlineData("Push", "push", -1)]
    public void PullAndPushTravelInOppositeDirections(string op, string key, int sign)
    {
        Hosted((session, app) =>
        {
            Invoke(app, "BeginRemoteOp", RemoteOp(op));
            session.PumpFrame();
            // Past the halfway point of a 0.75s pass, where the offset has a clear sign.
            AnimationManager.Tick(0.6f);
            session.PumpFrame();

            var icon = Button(app, key).Children[0];
            Assert.NotNull(icon.Transform);
            float y = icon.Transform!.Value.TranslateY;
            Assert.True(Math.Sign(y) == sign,
                $"{op} moved the wrong way (translateY {y})");

            Invoke(app, "EndRemoteOp");
        });
    }

    [Fact]
    public void TheMotionStopsWhenTheCommandDoes()
    {
        Hosted((session, app) =>
        {
            Invoke(app, "BeginRemoteOp", RemoteOp("Fetch"));
            session.PumpFrame();
            Invoke(app, "EndRemoteOp");
            session.PumpFrame();

            // Back to a still icon, and nothing left ticking that would keep the window
            // repainting for the rest of the session.
            var icon = Button(app, "fetch").Children[0];
            Assert.True(icon.Transform is null || icon.Transform.Value.IsIdentity);
        });
    }

    private static void Hosted(Action<HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        using var session = HeadlessSession.Create(1440, 900);
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
            session.PumpFrame();
            body(session, app);
        }
        finally { app?.Dispose(); }
    }
}
