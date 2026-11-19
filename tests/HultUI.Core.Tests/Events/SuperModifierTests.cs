using HultUI.Core.Builders;
using HultUI.Core.Events;
using Xunit;

namespace HultUI.Core.Tests.Events;

/// <summary>
/// Super is Ctrl only on a Mac. There ⌘ is the command key and has to work wherever the PC's
/// Ctrl does; on Linux and Windows Super is a key of its own, which the desktop mostly keeps and
/// an app may bind apart (Super+F5 is not Ctrl+F5). These run both rules on every platform, by
/// setting <see cref="KeyChord.SuperIsCtrl"/> for the length of one test.
/// </summary>
[Collection("KeyboardPlatform")]
public sealed class SuperModifierTests
{
    /// <summary>Runs the whole test under one platform's rule. Build every chord, map, FindBar and
    /// pan inside <paramref name="test"/>: a chord keeps the rule it was made under, so a map built
    /// under the Mac rule and queried under the PC rule answers for neither.</summary>
    private static void Under(bool superIsCtrl, Action test)
    {
        bool was = KeyChord.SuperIsCtrl;
        KeyChord.SuperIsCtrl = superIsCtrl;
        try { test(); }
        finally { KeyChord.SuperIsCtrl = was; }
    }

    private static KeyDownEvent Press(KeyCode key, KeyModifiers modifiers = KeyModifiers.None, int layoutKey = 0) =>
        new() { Key = key, Modifiers = modifiers, LayoutKey = layoutKey };

    private static KeyDownEvent Letter(char letter, KeyModifiers modifiers = KeyModifiers.None) =>
        Press((KeyCode)((int)KeyCode.A + (letter - 'a')), modifiers, letter);

    [Fact]
    public void TheDefault_FoldsOnlyOnTheMac()
    {
        Assert.Equal(OperatingSystem.IsMacOS(), KeyChord.SuperIsCtrl);
    }

    [Fact]
    public void OffTheMac_SuperIsAModifierOfItsOwn() => Under(false, () =>
    {
        var ctrl = new KeyChord(KeyCode.F5, KeyModifiers.Ctrl);
        var super = new KeyChord(KeyCode.F5, KeyModifiers.Super);

        Assert.False(ctrl.Matches(Press(KeyCode.F5, KeyModifiers.Super)));
        Assert.False(ctrl.Matches(Press(KeyCode.F5, KeyModifiers.Ctrl | KeyModifiers.Super)));
        Assert.True(super.Matches(Press(KeyCode.F5, KeyModifiers.Super)));
        Assert.False(super.Matches(Press(KeyCode.F5, KeyModifiers.Ctrl)));
        Assert.Equal(KeyModifiers.Super, super.Modifiers);
        Assert.NotEqual(ctrl, super);
    });

    [Fact]
    public void OnTheMac_SuperIsCtrl() => Under(true, () =>
    {
        var ctrl = new KeyChord(KeyCode.F5, KeyModifiers.Ctrl);
        var super = new KeyChord(KeyCode.F5, KeyModifiers.Super);

        Assert.True(ctrl.Matches(Press(KeyCode.F5, KeyModifiers.Super)));
        Assert.True(ctrl.Matches(Press(KeyCode.F5, KeyModifiers.Ctrl)));
        Assert.Equal(ctrl, super);
        Assert.Equal(KeyModifiers.Ctrl, super.Modifiers);
    });

    [Fact]
    public void LettersAndCharacters_KeepSuperApartOffTheMac() => Under(false, () =>
    {
        // A German keyboard: the key a US one has Y on types z.
        Assert.False(new KeyChord(KeyCode.Z, KeyModifiers.Ctrl).Matches(Press(KeyCode.Y, KeyModifiers.Super, 'z')));
        var plus = Press(KeyCode.Equals, KeyModifiers.Super | KeyModifiers.Shift);
        Assert.False(KeyChord.Of('+', KeyModifiers.Ctrl).Matches(plus));
        Assert.True(KeyChord.Of('+', KeyModifiers.Super).Matches(plus));
    });

    [Fact]
    public void LettersAndCharacters_TakeSuperAsCtrlOnTheMac() => Under(true, () =>
    {
        Assert.True(KeyChord.Of('+', KeyModifiers.Ctrl).Matches(Press(KeyCode.Equals, KeyModifiers.Super | KeyModifiers.Shift)));
    });

    [Theory]
    [InlineData("Super+F5")]
    [InlineData("Super+Shift+←")]
    [InlineData("Super+Ctrl+Alt+Shift+F5")]
    public void SuperChords_ReadBackToThemselvesOffTheMac(string text) => Under(false, () =>
    {
        Assert.Equal(text, KeyChord.Parse(text)?.ToString());
    });

    [Fact]
    public void WinAndMeta_AreSuper_AndCmdIsCtrl() => Under(false, () =>
    {
        Assert.Equal("Super+E", KeyChord.Parse("Win+E")?.ToString());
        Assert.Equal("Super+E", KeyChord.Parse("meta+e")?.ToString());
        Assert.Equal("Ctrl+P", KeyChord.Parse("cmd+p")?.ToString());
    });

    [Fact]
    public void OnTheMac_ASuperChordReadsAsCtrl() => Under(true, () =>
    {
        Assert.Equal("Ctrl+P", KeyChord.Parse("super+p")?.ToString());
    });

    [Theory]
    [InlineData(false, "Super+F5")]
    [InlineData(true, "Ctrl+F5")]
    public void ACapturedPress_KeepsSuperOnlyOffTheMac(bool superIsCtrl, string expected) => Under(superIsCtrl, () =>
    {
        Assert.Equal(expected, KeyChord.From(Press(KeyCode.F5, KeyModifiers.Super))?.ToString());
    });

    [Theory]
    [InlineData(false, KeyModifiers.Ctrl, true)]
    [InlineData(false, KeyModifiers.Ctrl | KeyModifiers.Super, true)]
    [InlineData(false, KeyModifiers.Ctrl | KeyModifiers.Alt, true)]
    [InlineData(false, KeyModifiers.Super, false)]
    [InlineData(false, KeyModifiers.Alt, false)]
    [InlineData(false, KeyModifiers.None, false)]
    [InlineData(true, KeyModifiers.Super, true)]
    [InlineData(true, KeyModifiers.Ctrl, true)]
    public void IsPrimaryHeld_IsCtrl_OrCommandOnTheMac(bool superIsCtrl, KeyModifiers held, bool primary) => Under(superIsCtrl, () =>
    {
        Assert.Equal(primary, KeyChord.IsPrimaryHeld(held));
    });

    // Super+K in a text field types nothing: it is the shortcut, under either rule, as Ctrl+K is.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASuperLetterInAField_IsAShortcut_NotTyping(bool superIsCtrl) => Under(superIsCtrl, () =>
    {
        var press = new KeyDownEvent
        {
            Key = KeyCode.K, Modifiers = KeyModifiers.Super, LayoutKey = 'k', Text = "k", TargetTakesText = true,
        };
        Assert.True(new KeyChord(KeyCode.K, KeyModifiers.Super).Matches(press));
    });

    private static ShortcutMap Refresh()
    {
        var map = new ShortcutMap();
        map.Add("Refresh", "Global", "Refresh", new KeyChord(KeyCode.F5, KeyModifiers.Ctrl)).CanRebind();
        map.Add("Other", "Global", "Other", new KeyChord(KeyCode.F9)).CanRebind();
        map.Add("ClipCopy", "Global", "Copy", new KeyChord(KeyCode.C, KeyModifiers.Ctrl));
        return map;
    }

    [Fact]
    public void AMap_KeepsSuperChordsApartOffTheMac() => Under(false, () =>
    {
        var map = Refresh();
        var superF5 = new KeyChord(KeyCode.F5, KeyModifiers.Super);

        Assert.Null(map.Find(Press(KeyCode.F5, KeyModifiers.Super)));
        Assert.Null(map.Holder(superF5, map["Other"]));
        Assert.True(map.Rebind("Other", superF5));
        Assert.Equal([new KeyChord(KeyCode.F5, KeyModifiers.Ctrl)], map["Refresh"].Keys);

        var saved = map.SaveOverrides();
        Assert.Equal("Super+F5", saved["Other"]);
        var fresh = Refresh();
        fresh.LoadOverrides(saved);
        Assert.Equal([superF5], fresh["Other"].Keys);
        Assert.Equal(KeyModifiers.Super, fresh["Other"].Keys[0].Modifiers);
    });

    [Fact]
    public void OnTheMac_CommandCCopies() => Under(true, () =>
    {
        Assert.True(Refresh().Matches("ClipCopy", Press(KeyCode.C, KeyModifiers.Super)));
    });

    [Fact]
    public void AFindBar_OpensOnCtrlF_NotSuperF_OffTheMac() => Under(false, () =>
    {
        var bar = new FindBar(() => { });
        Assert.False(bar.HandleKey(Letter('f', KeyModifiers.Super)));
        Assert.False(bar.IsOpen);
        Assert.True(bar.HandleKey(Letter('f', KeyModifiers.Ctrl)));
        Assert.True(bar.IsOpen);
    });

    [Fact]
    public void AnOpenFindBar_TakesNoSuperChordsForItsPlainKeys_OffTheMac() => Under(false, () =>
    {
        var bar = new FindBar(() => { }) { LetterKeys = true };
        Assert.True(bar.HandleKey(Letter('f', KeyModifiers.Ctrl)));
        bool matchCase = bar.Search.MatchCase, regex = bar.Search.UseRegex;

        Assert.False(bar.HandleKey(Letter('n', KeyModifiers.Super)));
        Assert.False(bar.HandleKey(Letter('p', KeyModifiers.Super)));
        Assert.False(bar.HandleKey(Letter('c', KeyModifiers.Super | KeyModifiers.Alt)));
        Assert.False(bar.HandleKey(Letter('r', KeyModifiers.Super | KeyModifiers.Alt)));
        Assert.Equal(matchCase, bar.Search.MatchCase);
        Assert.Equal(regex, bar.Search.UseRegex);

        // The control: the same bar does take the plain keys.
        Assert.True(bar.HandleKey(Letter('n')));
        Assert.True(bar.HandleKey(Letter('c', KeyModifiers.Alt)));
    });

    [Fact]
    public void OnTheMac_CommandFOpensTheFindBar_AndCommandNIsNotN() => Under(true, () =>
    {
        var bar = new FindBar(() => { }) { LetterKeys = true };
        Assert.True(bar.HandleKey(Letter('f', KeyModifiers.Super)));
        Assert.True(bar.IsOpen);
        Assert.False(bar.HandleKey(Letter('n', KeyModifiers.Super)));
    });

    private static HorizontalPan Pan() => new(() => { }) { ContentWidth = 1000, ViewportWidth = 400 };

    [Fact]
    public void APan_IgnoresSuperWithAnArrow_OffTheMac() => Under(false, () =>
    {
        var pan = Pan();
        Assert.False(pan.HandleKey(Press(KeyCode.Right, KeyModifiers.Super)));
        Assert.False(pan.HandleKey(Press(KeyCode.Left, KeyModifiers.Super)));
        Assert.Equal(0, pan.Offset);
    });

    [Fact]
    public void APan_JumpsOnCtrlArrow_AndStepsOnAltArrow_OffTheMac() => Under(false, () =>
    {
        var jump = Pan();
        Assert.True(jump.HandleKey(Press(KeyCode.Right, KeyModifiers.Ctrl)));
        Assert.Equal(600, jump.Offset);

        var step = Pan();
        Assert.True(step.HandleKey(Press(KeyCode.Right, KeyModifiers.Alt)));
        Assert.Equal(HorizontalPan.KeyStep, step.Offset);
    });

    [Fact]
    public void OnTheMac_CommandArrowJumps() => Under(true, () =>
    {
        var pan = Pan();
        Assert.True(pan.HandleKey(Press(KeyCode.Right, KeyModifiers.Super)));
        Assert.Equal(600, pan.Offset);
    });
}
