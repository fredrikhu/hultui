using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// <see cref="FindBar"/> hosted the way the apps host it: a read-only body of text, the bar over
/// it while open, the view's keys routed through <see cref="FindBar.HandleKey"/> at its root and
/// <see cref="FindBar.Service"/> called after every build.
/// </summary>
public class FindBarTests
{
    private sealed class Viewer : Component
    {
        public readonly FindBar Find;
        public readonly Ref<TextAreaElement> Body = new();
        public readonly string[] Lines;
        private readonly int[] _starts;
        public int RootKeys;
        public int Builds;

        public Viewer(int lineCount = 200)
        {
            Lines = Enumerable.Range(0, lineCount)
                .Select(i => i % 50 == 49 ? $"line {i} has the needle in it" : $"line {i}")
                .ToArray();
            _starts = new int[Lines.Length];
            for (int i = 1; i < Lines.Length; i++) _starts[i] = _starts[i - 1] + Lines[i - 1].Length + 1;

            Find = new FindBar(MarkDirty)
            {
                Placeholder = "Find in text…",
                Body = () => Body.Current,
                Reveal = () =>
                {
                    if (Find!.Search.Current is { } hit) Body.Current?.ScrollOffsetIntoView(_starts[hit.Line] + hit.Start);
                },
                LetterKeys = true,
                Selection = () =>
                {
                    if (Body.Current is not { HasSelection: true } area) return null;
                    int line = Array.BinarySearch(_starts, area.SelectionStart);
                    if (line < 0) line = ~line - 1;
                    return new FindSelection(area.SelectedText, line, 0, area.SelectionStart - _starts[line]);
                },
            };
            Find.Search.Bind(Lines.Length, 1, (line, _) => line >= 0 && line < Lines.Length ? Lines[line] : null);
        }

        public override Element Build()
        {
            Builds++;
            var root = Flex.Column().Width(500f).Height(300f);
            root.On<KeyDownEvent>(e =>
            {
                if (Find.HandleKey(e)) { e.StopPropagation = true; return; }
                RootKeys++;
            });
            if (Find.IsOpen) root.AddChild(Find.Build());
            var body = TextArea.New(string.Join('\n', Lines)).ReadOnly().FlexGrow(1).FlexBasis(0f).WithRef(Body);
            root.AddChild(body);
            return root;
        }

        protected internal override void OnAfterBuild() => Find.Service();
    }

    private static (HeadlessSession Session, Viewer View) Open(int lines = 200)
    {
        var session = HeadlessSession.Create(500, 300);
        var view = new Viewer(lines);
        session.Window.SetRoot(view.Mount());
        session.PumpFrame();
        view.Body.Current!.Focus();
        session.PumpFrame();
        return (session, view);
    }

    private static void Press(HeadlessSession s, KeyCode key, KeyModifiers mods = KeyModifiers.None)
    {
        s.Window.PressKey(key, mods);
        s.PumpFrames(2);
    }

    private static string Status(HeadlessSession s) =>
        ((TextElement)s.Window.FindByAutomationId("find-status")!.Element).Content;

    [Fact]
    public void CtrlFOpensTheBarWithTheKeyboardInIt_AndTypingSearches()
    {
        var (s, view) = Open();
        using var _ = s;

        Press(s, KeyCode.F, KeyModifiers.Ctrl);
        Assert.True(view.Find.IsOpen);
        var field = s.Window.FindByAutomationId("find-input")!.Element;
        Assert.Same(field, s.Window.Window.FocusedElement);

        s.Window.TypeText("needle");
        s.PumpFrames(2);
        Assert.Equal(4, view.Find.Search.Hits.Count);
        Assert.Equal("1 of 4", Status(s));
    }

    [Fact]
    public void EnterAndShiftEnterWalkTheMatches_AndTheMatchIsScrolledTo()
    {
        var (s, view) = Open();
        using var _ = s;
        Press(s, KeyCode.F, KeyModifiers.Ctrl);
        s.Window.TypeText("needle");
        s.PumpFrames(2);

        Press(s, KeyCode.Enter);
        Press(s, KeyCode.Enter);
        Assert.Equal(2, view.Find.Search.Index);
        // Line 149: far below a 300px window, so it is on screen only if the bar scrolled to it.
        Assert.True(view.Body.Current!.ScrollOffsetY > 1000, $"not scrolled to the match ({view.Body.Current.ScrollOffsetY})");

        Press(s, KeyCode.Enter, KeyModifiers.Shift);
        Assert.Equal(1, view.Find.Search.Index);
        Assert.Equal("2 of 4", Status(s));
    }

    /// <summary>Escape closes the bar, clears the query, and the keyboard goes back to the body —
    /// left on the unmounted field, the view's keys would reach nothing.</summary>
    [Fact]
    public void EscapeClosesAndGivesTheBodyTheKeyboard()
    {
        var (s, view) = Open();
        using var _ = s;
        Press(s, KeyCode.F, KeyModifiers.Ctrl);
        s.Window.TypeText("needle");

        Press(s, KeyCode.Escape);

        Assert.False(view.Find.IsOpen);
        Assert.Equal("", view.Find.Search.Query);
        Assert.Null(s.Window.FindByAutomationId("find-input"));
        Assert.Same(view.Body.Current, s.Window.Window.FocusedElement);
    }

    [Fact]
    public void AltCAndAltRToggleTheOptions_AndTheChipsShowIt()
    {
        var (s, view) = Open();
        using var _ = s;
        Press(s, KeyCode.F, KeyModifiers.Ctrl);

        Press(s, KeyCode.C, KeyModifiers.Alt);
        Press(s, KeyCode.R, KeyModifiers.Alt);

        Assert.True(view.Find.Search.MatchCase);
        Assert.True(view.Find.Search.UseRegex);
        var chip = (ButtonElement)s.Window.FindByAutomationId("find-case")!.Element;
        Assert.Equal(ButtonVariant.Primary, chip.Variant);

        s.Window.FindByAutomationId("find-case")!.Click();
        s.PumpFrames(2);
        Assert.False(view.Find.Search.MatchCase);
        Assert.Equal(ButtonVariant.Secondary, ((ButtonElement)s.Window.FindByAutomationId("find-case")!.Element).Variant);
    }

    /// <summary>n and p walk the matches while the body has the keyboard, and are letters in the field.</summary>
    [Fact]
    public void NAndPWalkTheMatchesFromTheBody_ButAreLettersInTheField()
    {
        var (s, view) = Open();
        using var _ = s;
        Press(s, KeyCode.F, KeyModifiers.Ctrl);
        s.Window.TypeText("needle");
        s.PumpFrames(2);

        // In the field n is a letter, not a step. The key down is what the bar sees; its text
        // goes to the field as any letter does.
        Press(s, KeyCode.N);
        Assert.Equal(0, view.Find.Search.Index);

        view.Body.Current!.Focus();
        s.PumpFrame();
        Press(s, KeyCode.N);
        Assert.Equal(1, view.Find.Search.Index);
        Press(s, KeyCode.P);
        Assert.Equal(0, view.Find.Search.Index);
    }

    /// <summary>Ctrl+F with text selected searches for it, starting at the selection rather than
    /// the first occurrence — opening the bar must not scroll the reader away from what they chose.</summary>
    [Fact]
    public void CtrlFSearchesForTheSelection_StartingAtIt()
    {
        var (s, view) = Open();
        using var _ = s;
        var area = view.Body.Current!;
        int third = string.Join('\n', view.Lines.Take(149)).Length + 1 + "line 149 has the ".Length;
        area.CursorPosition = third;
        for (int i = 0; i < "needle".Length; i++)
            s.Window.PressKey(KeyCode.Right, KeyModifiers.Shift);
        s.PumpFrame();

        Press(s, KeyCode.F, KeyModifiers.Ctrl);

        Assert.Equal("needle", view.Find.Search.Query);
        Assert.Equal(2, view.Find.Search.Index);
    }

    /// <summary>
    /// Every step of the bar builds its owner once, and that build reveals the match: the search the
    /// bar made notifies the owner through the bar, which holds it for the step's own notice. A
    /// step used to build twice — the search's notice, then the bar's — the first build before the
    /// step had asked for its reveal; opening with a selection built three times.
    /// </summary>
    [Fact]
    public void EachStepBuildsTheOwnerOnce_AndThatBuildRevealsTheMatch()
    {
        var (s, view) = Open();
        using var _ = s;
        var area = view.Body.Current!;
        int third = string.Join('\n', view.Lines.Take(149)).Length + 1 + "line 149 has the ".Length;
        area.CursorPosition = third;
        for (int i = 0; i < "needle".Length; i++)
            s.Window.PressKey(KeyCode.Right, KeyModifiers.Shift);
        s.PumpFrame();
        area.ScrollOffsetIntoView(0);
        s.PumpFrame();

        int StepBuilds(Action step)
        {
            int before = view.Builds;
            step();
            return view.Builds - before;
        }

        // Ctrl+F with "needle" selected: the query, the seek to the selection and the bar opening.
        Assert.Equal(1, StepBuilds(() => s.Window.PressKey(KeyCode.F, KeyModifiers.Ctrl)));
        s.PumpFrames(2);
        Assert.Equal(2, view.Find.Search.Index);
        float atThird = area.ScrollOffsetY;
        Assert.True(atThird > 1000, $"opening did not reveal the selection's match ({atThird})");

        Assert.Equal(1, StepBuilds(() => s.Window.PressKey(KeyCode.Enter)));
        s.PumpFrames(2);
        Assert.Equal(3, view.Find.Search.Index);
        float atFourth = area.ScrollOffsetY;
        Assert.True(atFourth > atThird, $"Enter did not reveal the match ({atThird} → {atFourth})");

        Assert.Equal(1, StepBuilds(() => s.Window.PressKey(KeyCode.Enter, KeyModifiers.Shift)));
        s.PumpFrames(2);
        Assert.Equal(2, view.Find.Search.Index);
        Assert.True(area.ScrollOffsetY < atFourth, $"Shift+Enter did not reveal the match ({atFourth} → {area.ScrollOffsetY})");

        Assert.Equal(1, StepBuilds(() => s.Window.PressKey(KeyCode.C, KeyModifiers.Alt)));
        s.PumpFrames(2);
        Assert.True(view.Find.Search.MatchCase);
        Assert.Equal(1, StepBuilds(() => s.Window.PressKey(KeyCode.R, KeyModifiers.Alt)));
        s.PumpFrames(2);
        Assert.True(view.Find.Search.UseRegex);
        Assert.Equal(1, StepBuilds(() => s.Window.FindByAutomationId("find-next")!.Click()));
        s.PumpFrames(2);

        area.Focus();
        s.PumpFrame();
        Assert.Equal(1, StepBuilds(() => s.Window.PressKey(KeyCode.N)));
        s.PumpFrames(2);
        Assert.Equal(1, StepBuilds(() => s.Window.PressKey(KeyCode.Escape)));
        s.PumpFrames(2);
        Assert.False(view.Find.IsOpen);
        Assert.Same(area, s.Window.Window.FocusedElement);

        // Typing still builds once a character: the search tells the owner through the bar.
        s.Window.PressKey(KeyCode.F, KeyModifiers.Ctrl);
        s.PumpFrames(2);
        Assert.Equal(1, StepBuilds(() => s.Window.TypeText("x")));
    }

    /// <summary>Ctrl+F on an open bar puts the keyboard back in the field with the query selected
    /// whole: the next keystroke is a new search, not an addition to the old one.</summary>
    [Fact]
    public void CtrlFOnAnOpenBarSelectsTheQuery()
    {
        var (s, view) = Open();
        using var _ = s;
        Press(s, KeyCode.F, KeyModifiers.Ctrl);
        s.Window.TypeText("needle");
        view.Body.Current!.Focus();
        s.PumpFrame();

        Press(s, KeyCode.F, KeyModifiers.Ctrl);
        s.Window.TypeText("line 1");
        s.PumpFrames(2);

        Assert.Equal("line 1", view.Find.Search.Query);
    }

    [Fact]
    public void TheThemeReachesTheBar()
    {
        var (s, view) = Open();
        using var _ = s;
        s.Application.Theme = Theme.Dark.With(new FindBarTheme
        {
            Background = Color.FromHex("#102030"),
            StatusErrorColor = Color.FromHex("#FF00AA"),
            ToggleOn = new ButtonStyle { Background = Color.FromHex("#00AA00") },
        });
        Press(s, KeyCode.F, KeyModifiers.Ctrl);
        s.Window.TypeText("haystack");
        Press(s, KeyCode.C, KeyModifiers.Alt);

        var bar = s.Window.FindByAutomationId("find-input")!.Element.Parent!;
        Assert.Equal(Color.FromHex("#102030"), ((BoxElement)bar).BackgroundColor);
        Assert.Equal("no matches", Status(s));
        Assert.Equal(Color.FromHex("#FF00AA"), s.Window.FindByAutomationId("find-status")!.Element.ResolvedInherited.TextColor);
        Assert.Equal(Color.FromHex("#00AA00"), ((ButtonElement)s.Window.FindByAutomationId("find-case")!.Element).BackgroundColor);
    }
}
