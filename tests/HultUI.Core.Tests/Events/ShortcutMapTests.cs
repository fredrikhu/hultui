using HultUI.Core.Events;
using Xunit;

namespace HultUI.Core.Tests.Events;

/// <summary>
/// <see cref="ShortcutMap"/>: one table that dispatch, display and rebinding all read, so none of
/// them can disagree. The rebinding cases are Vesper's rules, now the framework's.
/// </summary>
public sealed class ShortcutMapTests
{
    private static KeyDownEvent Press(KeyCode key, KeyModifiers modifiers = KeyModifiers.None) =>
        new() { Key = key, Modifiers = modifiers };

    private static ShortcutMap Commander()
    {
        var map = new ShortcutMap();
        map.Add("Copy", "Commands", "Copy", new KeyChord(KeyCode.F5)).CanRebind();
        map.Add("Move", "Commands", "Move", new KeyChord(KeyCode.F6)).CanRebind();
        map.Add("Rename", "Commands", "Rename", new KeyChord(KeyCode.F6, KeyModifiers.Shift)).CanRebind();
        map.Add("ClipCopy", "Commands", "Copy to the clipboard", new KeyChord(KeyCode.C, KeyModifiers.Ctrl));
        map.Add("Close", "Viewer", "Close", new KeyChord(KeyCode.F3), new KeyChord(KeyCode.Escape)).CanRebind();
        return map;
    }

    [Fact]
    public void AKeyIsFoundInTableOrder_WithinItsGroup()
    {
        var map = Commander();
        Assert.Equal("Move", map.Find(Press(KeyCode.F6))?.Id);
        Assert.Equal("Rename", map.Find(Press(KeyCode.F6, KeyModifiers.Shift))?.Id);
        Assert.Null(map.Find(Press(KeyCode.F6, KeyModifiers.Ctrl)));
        Assert.Equal("Close", map.Find(Press(KeyCode.Escape), "Viewer")?.Id);
        Assert.Null(map.Find(Press(KeyCode.Escape), "Commands"));
        Assert.True(map.Matches("ClipCopy", Press(KeyCode.C, KeyModifiers.Super)));
    }

    [Fact]
    public void ItsText_IsTheBinding()
    {
        var map = Commander();
        Assert.Equal("F3 · Esc", map.Keys("Close"));
        map.Add("Views", "Global", "Overview … Files", new KeyChord(KeyCode.Digit1)).Shown("1 – 7");
        Assert.Equal("1 – 7", map.Keys("Views"));

        map.Rebind("Copy", new KeyChord(KeyCode.F2));
        Assert.Equal("F2", map.Keys("Copy"));   // the text moves with the key
    }

    [Fact]
    public void ALegend_IsTheGroupsWordedShortcuts_InOrder()
    {
        var map = new ShortcutMap();
        map.Add("mr.search", "Merge requests", "Search", new KeyChord(KeyCode.Slash)).Legend("search");
        map.Add("mr.open", "Merge requests", "Open in the browser", new KeyChord(KeyCode.O));
        map.Add("mr.approve", "Merge requests", "Approve", new KeyChord(KeyCode.A)).Legend("approve");
        map.Add("todo.done", "To do", "Mark done", new KeyChord(KeyCode.D)).Legend("done");

        Assert.Equal([("/", "search"), ("a", "approve")], map.Legend("Merge requests"));
        Assert.Equal(["Merge requests", "To do"], map.Groups);
    }

    [Fact]
    public void Rebinding_TakesTheChordFromItsHolder_WhichGoesBackToItsOwnDefault()
    {
        var map = Commander();
        map.Rebind("Move", new KeyChord(KeyCode.F9));
        map.Rebind("Copy", new KeyChord(KeyCode.F9));

        Assert.Equal([new KeyChord(KeyCode.F9)], map["Copy"].Keys);
        Assert.Equal([new KeyChord(KeyCode.F6)], map["Move"].Keys);
    }

    [Fact]
    public void Rebinding_ontoAHoldersOwnDefault_LeavesItWithNoKey()
    {
        var map = Commander();
        map.Rebind("Copy", new KeyChord(KeyCode.F6));

        Assert.Empty(map["Move"].Keys);
        Assert.Equal("", map.Keys("Move"));
        Assert.Equal("Copy", map.Find(Press(KeyCode.F6))?.Id);
    }

    [Fact]
    public void Rebinding_ontoAKeyTheAppKeptForItself_IsRefused()
    {
        // Vesper let a command take Ctrl+C: either it never fired (checked after the fixed
        // chord) or it silently replaced copying (checked before).
        var map = Commander();
        Assert.False(map.Rebind("Copy", new KeyChord(KeyCode.C, KeyModifiers.Ctrl)));
        Assert.Equal([new KeyChord(KeyCode.F5)], map["Copy"].Keys);
        Assert.Equal("ClipCopy", map.Holder(new KeyChord(KeyCode.C, KeyModifiers.Ctrl), map["Copy"])?.Id);
    }

    [Fact]
    public void AnotherGroup_IsNoConflict()
    {
        var map = Commander();
        Assert.True(map.Rebind("Copy", new KeyChord(KeyCode.F3)));
        Assert.Equal("Close", map.Find(Press(KeyCode.F3), "Viewer")?.Id);
        Assert.Equal("Copy", map.Find(Press(KeyCode.F3), "Commands")?.Id);
    }

    [Fact]
    public void OnlyChangesAreSaved_AndTheyReadBack()
    {
        var map = Commander();
        Assert.Empty(map.SaveOverrides());

        map.Rebind("Copy", new KeyChord(KeyCode.F6));   // Move left with nothing
        var saved = map.SaveOverrides();
        Assert.Equal(new Dictionary<string, string> { ["Copy"] = "F6", ["Move"] = "" }, saved);

        var next = Commander();
        next.LoadOverrides(saved);
        Assert.Equal("Copy", next.Find(Press(KeyCode.F6))?.Id);
        Assert.Empty(next["Move"].Keys);
    }

    [Fact]
    public void AFileEditedByHand_CannotMakeOneKeyMeanTwoThings()
    {
        var map = Commander();
        map.LoadOverrides(new Dictionary<string, string>
        {
            ["Copy"] = "Ctrl+C",         // the app's own key: dropped
            ["Rename"] = "F6",           // Move's default, Move not unbound: the earlier one keeps it
            ["Gone"] = "F1",             // no such shortcut
            ["Move"] = "Ctrl+Nonsense",  // does not parse: default
            ["ClipCopy"] = "F12",        // not rebindable
        });

        Assert.Equal([new KeyChord(KeyCode.F5)], map["Copy"].Keys);
        Assert.Equal([new KeyChord(KeyCode.F6)], map["Move"].Keys);
        Assert.Equal([new KeyChord(KeyCode.F6, KeyModifiers.Shift)], map["Rename"].Keys);
        Assert.Equal([new KeyChord(KeyCode.C, KeyModifiers.Ctrl)], map["ClipCopy"].Keys);
    }

    [Fact]
    public void LoadingStartsFromTheDefaults()
    {
        var map = Commander();
        map.Rebind("Copy", new KeyChord(KeyCode.F2));
        map.LoadOverrides(new Dictionary<string, string>());
        Assert.Equal([new KeyChord(KeyCode.F5)], map["Copy"].Keys);
    }

    [Fact]
    public void ADraft_LeavesTheLiveMapAlone()
    {
        var live = Commander();
        var draft = live.Clone();
        draft.Rebind("Copy", new KeyChord(KeyCode.F2));

        Assert.Equal("F5", live.Keys("Copy"));
        live.LoadOverrides(draft.SaveOverrides());
        Assert.Equal("F2", live.Keys("Copy"));
        Assert.True(live["Copy"].Rebindable);
    }

    [Fact]
    public void AnIdIsAddedOnce_AndOnlyARebindableShortcutRebinds()
    {
        var map = Commander();
        Assert.Throws<ArgumentException>(() => map.Add("Copy", "Commands", "Again"));
        Assert.Throws<InvalidOperationException>(() => map.Rebind("ClipCopy", new KeyChord(KeyCode.F1)));
        Assert.Throws<KeyNotFoundException>(() => map["Nope"]);
    }
}
