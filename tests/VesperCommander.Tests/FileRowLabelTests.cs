using System.IO;
using System.Reflection;
using HultUI.Core.Accessibility;
using HultUI.Headless;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// The file list names its rows for a screen reader. It set no label, so every row was read
/// out as the FileEntry record's ToString() — "FileEntry { Name = notes.txt, FullPath = … }".
/// </summary>
public class FileRowLabelTests
{
    [Fact]
    public void RowsAreNamedByWhatTheyAre()
    {
        var root = Path.Combine(Path.GetTempPath(), "vesper-labels-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "alpha"));
        File.WriteAllText(Path.Combine(root, "notes.txt"), "hello");
        try
        {
            using var session = HeadlessSession.Create(1240, 820);
            var app = new CommanderApp(session.Application, session.Window.Window, root);
            session.Application.SetRoot(app.Mount());
            session.PumpFrame();
            typeof(CommanderApp).GetMethod("NavigateTo", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(app, ['L', root, null, false]);
            session.PumpFrames(3);

            var names = Walk(session.Window.AccessibilityTree)
                .Where(n => n.Role == AccessibilityRole.ListItem).Select(n => n.Name ?? "").ToList();
            Assert.Contains("alpha, folder", names);
            Assert.Contains(names, n => n.StartsWith("notes.txt, 5 B, modified ", System.StringComparison.Ordinal));
            Assert.DoesNotContain(names, n => n.StartsWith("FileEntry", System.StringComparison.Ordinal));
            (app as System.IDisposable)?.Dispose();
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* best-effort temp cleanup */ }
        }
    }

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node) =>
        node.Children.SelectMany(Walk).Prepend(node);
}
