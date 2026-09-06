using System.Reflection;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// What the Files browser makes of a blob's bytes: text, or the binary placeholder.
///
/// <para>The browser shows a blob rather than a diff, so it is free to read encodings git's
/// diff would call binary. UTF-16 with a byte-order mark is the one that matters: Windows
/// writes its own <c>desktop.ini</c> that way, and the NUL after every ASCII character used
/// to trip the binary scan.</para>
/// </summary>
[Collection("JunctionDiff")]
public class FilesEncodingTests
{
    [Theory]
    [InlineData("utf-16LE")]
    [InlineData("utf-16BE")]
    [InlineData("utf-8")]
    public void AByteOrderMarkedFileIsShownAsText(string encodingName)
    {
        var enc = System.Text.Encoding.GetEncoding(encodingName);
        var bytes = enc.GetPreamble().Concat(enc.GetBytes("[.ShellClassInfo]\r\nIconResource=x\r\n")).ToArray();

        Hosted("desktop.ini", bytes, (session, app) =>
        {
            session.PumpUntil(() => BodyOrNull(app) is { } b && b.Text.Length > 0, TimeSpan.FromSeconds(30));

            Assert.Equal("[.ShellClassInfo]\nIconResource=x", BodyOrNull(app)!.Text);
        });
    }

    [Fact]
    public void ANulWithoutAMarkIsStillBinary()
    {
        Hosted("blob.bin", [(byte)'a', 0, (byte)'b'], (session, app) =>
        {
            session.PumpUntil(() => Contains(session, "Binary file"), TimeSpan.FromSeconds(30));

            Assert.Null(BodyOrNull(app));
        });
    }

    private static void Hosted(string name, byte[] bytes, Action<HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.WriteBytes(name, bytes);
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "add the file");

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

            session.Window.PressKey(KeyCode.Digit7);   // Files
            session.PumpUntil(() => app!.View == JunctionView.Files, TimeSpan.FromSeconds(20));

            typeof(JunctionApp).GetField("_filesSelPath", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(app, name);
            typeof(JunctionApp).GetMethod("MarkDirty",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.Invoke(app, null);

            body(session, app);
        }
        finally { app?.Dispose(); }
    }

    private static TextAreaElement? BodyOrNull(JunctionApp app) =>
        ((HultUI.Core.Reactivity.Ref<TextAreaElement>)typeof(JunctionApp)
            .GetField("_fileBodyRef", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app)!).Current;

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
}
