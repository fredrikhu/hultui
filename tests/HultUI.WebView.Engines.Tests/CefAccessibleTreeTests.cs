using System.Text;
using System.Text.Json;
using HultUI.Core.Accessibility;
using HultUI.WebView.Cef;
using Xunit;
using Xunit.Abstractions;

namespace HultUI.WebView.Engines.Tests;

/// <summary>
/// Step 5.6: the mirror of a page's accessibility tree, fed what CEF sent the spike for a
/// small form (spike L0, question 9): the full tree, then a change, then locations.
/// </summary>
public class CefAccessibleTreeTests(ITestOutputHelper output)
{
    private static object? Recorded(string file) =>
        CefAccessibleTree.FromJson(JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", file))).RootElement);

    /// <summary>The first recorded callback of a file: the files hold every callback in order.</summary>
    private static object? First(string file) => ((List<object?>)Recorded(file)!)[0];

    internal static string Describe(IReadOnlyList<AccessibleContentNode> nodes)
    {
        var text = new StringBuilder();
        void Walk(AccessibleContentNode n, int depth)
        {
            text.Append(' ', depth * 2).Append(n.Role).Append(" \"").Append(n.Name).Append('"');
            if (n.Value is not null) text.Append(" value=\"").Append(n.Value).Append('"');
            if (n.IsToggled is { } t) text.Append(t ? " checked" : " unchecked");
            if (n.IsFocusable) text.Append(" focusable");
            if (n.IsFocused) text.Append(" focused");
            text.Append($" @{n.Bounds.X:0},{n.Bounds.Y:0} {n.Bounds.Width:0}x{n.Bounds.Height:0} #{n.Key}").Append('\n');
            foreach (var c in n.Children) Walk(c, depth + 1);
        }
        foreach (var n in nodes) Walk(n, 0);
        return text.ToString();
    }

    private const string Form = """
        Document "A11y form" focusable focused @0,0 1200x900 #20
          Heading "Settings" @12,32 1176x65 #26
          Label "Name " @12,129 74x33 #10
          TextInput "Name" value="Ada" focusable @86,130 308x36 #6
          Checkbox "Remember me" unchecked focusable @406,136 20x20 #18
          Button "Press me" focusable @610,130 110x36 #30
          List "" @12,190 1176x66 #31
            ListItem "" @72,190 1116x33 #32
              Label "One" @72,190 47x33 #15
            ListItem "" @72,223 1116x33 #33
              Link "Two" focusable @72,223 46x33 #34
          Image "A red square" @12,291 15x15 #21

        """;

    [Fact]
    public void TheFirstTree_IsThePagesControls_WithoutChromiumsBoxes()
    {
        var tree = new CefAccessibleTree();
        Assert.True(tree.ApplyTreeChange(First("q9-tree-form-first.json")));
        var text = Describe(tree.Build());
        output.WriteLine(text);
        // Ignored containers, inline text boxes and list markers are gone; a button's and a
        // link's own text is their name, not a child; the field's text is its value.
        Assert.Equal(Form.ReplaceLineEndings("\n"), text);
    }

    [Fact]
    public void AnUpdate_ChangesOnlyWhatItSends()
    {
        var tree = new CefAccessibleTree();
        tree.ApplyTreeChange(First("q9-tree-form-first.json"));
        var before = tree.Build();
        Assert.True(tree.ApplyTreeChange(First("q9-tree-form-change.json")));
        var after = Describe(tree.Build());
        // The heading's text and the check box's state changed; the rest is as it was.
        Assert.Equal(
            Form.Replace("Heading \"Settings\"", "Heading \"Preferences\"").Replace("unchecked focusable", "checked focusable").ReplaceLineEndings("\n"),
            after);
        Assert.Equal(before.Count, tree.Build().Count);
    }

    [Fact]
    public void ALocationChange_MovesTheNodes()
    {
        var tree = new CefAccessibleTree();
        tree.ApplyTreeChange(First("q9-tree-form-first.json"));
        tree.ApplyTreeChange(First("q9-tree-form-change.json"));
        Assert.True(tree.ApplyLocationChange(First("q9-location-change.json")));
        var page = Assert.Single(tree.Build());
        // The spike's change moved the page down 28 pixels (the heading to 60, the button to 157.66).
        Assert.Equal(60, page.Children.Single(n => n.Role == AccessibilityRole.Heading).Bounds.Y, 0.01);
        Assert.Equal(157.66, page.Children.Single(n => n.Role == AccessibilityRole.Button).Bounds.Y, 0.01);
    }

    [Fact]
    public void AnotherTreesUpdates_AndAnUnknownShape_AreIgnored()
    {
        var tree = new CefAccessibleTree();
        tree.ApplyTreeChange(First("q9-tree-form-first.json"));
        var frame = new Dictionary<string, object?>
        {
            ["ax_tree_id"] = "AN-IFRAME",
            ["updates"] = new List<object?>
            {
                new Dictionary<string, object?>
                {
                    ["tree_data"] = new Dictionary<string, object?> { ["parent_tree_id"] = "6F5715D1BDC4D68A6461E9C9D8CEA655" },
                    ["root_id"] = 1,
                    ["nodes"] = new List<object?> { new Dictionary<string, object?> { ["id"] = 1, ["role"] = "rootWebArea" } },
                },
            },
        };
        Assert.False(tree.ApplyTreeChange(frame));
        Assert.False(tree.ApplyTreeChange("nonsense"));
        Assert.False(tree.ApplyLocationChange(null));
        Assert.Equal(Form.ReplaceLineEndings("\n"), Describe(tree.Build()));
    }

    [Fact]
    public void ANewMainTree_ReplacesTheOld()
    {
        var tree = new CefAccessibleTree();
        tree.ApplyTreeChange(First("q9-tree-form-first.json"));
        var next = new Dictionary<string, object?>
        {
            ["ax_tree_id"] = "NEXT-PAGE",
            ["updates"] = new List<object?>
            {
                new Dictionary<string, object?>
                {
                    ["tree_data"] = new Dictionary<string, object?> { ["focus_id"] = 1 },
                    ["root_id"] = 1,
                    ["nodes"] = new List<object?>
                    {
                        new Dictionary<string, object?>
                        {
                            ["id"] = 1, ["role"] = "rootWebArea", ["child_ids"] = new List<object?>(),
                            ["attributes"] = new Dictionary<string, object?> { ["name"] = "Next" },
                            ["location"] = new Dictionary<string, object?> { ["x"] = 0, ["y"] = 0, ["width"] = 10, ["height"] = 10 },
                        },
                    },
                },
            },
        };
        Assert.True(tree.ApplyTreeChange(next));
        Assert.Equal("Document \"Next\" focused @0,0 10x10 #1\n", Describe(tree.Build()));
        Assert.Equal(1, tree.Count);
    }

    [Theory]
    [InlineData("button", null, AccessibilityRole.Button)]
    [InlineData("toggleButton", "true", AccessibilityRole.Switch)]
    [InlineData("textField", null, AccessibilityRole.TextInput)]
    [InlineData("comboBoxSelect", null, AccessibilityRole.ComboBox)]
    [InlineData("menuListOption", null, AccessibilityRole.ListItem)]
    [InlineData("rootWebArea", null, AccessibilityRole.Document)]
    [InlineData("staticText", null, AccessibilityRole.Label)]
    [InlineData("somethingNew", null, AccessibilityRole.Group)]
    public void ChromiumsRoles_AreHultUIs(string role, string? checkedState, AccessibilityRole expected) =>
        Assert.Equal(expected, CefAccessibleTree.Role(role, checkedState));
}
