using System.Reflection;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Find-in-text inside the F3 comparison view.
///
/// <para>What this has that the file viewer's search does not is a second column. The two
/// sides share one set of row numbers, so a match has to say which side it is on, and a search
/// has to cross both — looking for a name in a comparison and being shown only the left-hand
/// occurrences would be worse than not searching at all.</para>
/// </summary>
public class DiffFindTests
{
    [Fact]
    public void CtrlFOpensTheBarAndFocusesIt()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "alpha\n");
        t.WriteRight("a.txt", "beta\n");
        t.Open();
        t.OpenDiffOn("a.txt");

        t.Press(KeyCode.F, KeyModifiers.Ctrl);

        Assert.True(FindOpen(t));
        Assert.IsType<TextInputElement>(t.Focused);
    }

    /// <summary>
    /// Both columns are searched, and each match records its side. Lane 0 is the left file,
    /// lane 1 the right.
    /// </summary>
    [Fact]
    public void BothSidesAreSearched()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "needle one\nplain\n");
        t.WriteRight("a.txt", "needle two\nneedle three\n");
        t.Open();
        t.OpenDiffOn("a.txt");

        Find(t, "needle");

        var hits = Search(t).Hits;
        Assert.Equal(3, hits.Count);
        Assert.Contains(hits, h => h.Lane == 0);
        Assert.Contains(hits, h => h.Lane == 1);
        // Reading order: everything on the earlier row first, left column before right.
        Assert.True(hits[0].Lane == 0 && hits[1].Lane == 1,
            "the first row's two sides did not come back left-then-right");
    }

    /// <summary>A row where only one side has text — an addition or a deletion — contributes
    /// matches from that side only, rather than searching the blank half as an empty line.</summary>
    [Fact]
    public void TheBlankHalfOppositeAnAdditionIsSkipped()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "shared\n");
        t.WriteRight("a.txt", "shared\nadded needle\n");
        t.Open();
        t.OpenDiffOn("a.txt");

        Find(t, "needle");

        var hits = Search(t).Hits;
        Assert.Single(hits);
        Assert.Equal(1, hits[0].Lane);      // the right side, where the added line is
    }

    [Fact]
    public void MatchesAreTintedInTheColumnTheyAreIn()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "needle left\n");
        t.WriteRight("a.txt", "plain right\n");
        t.Open();
        t.OpenDiffOn("a.txt");

        Find(t, "needle");

        var (_, left) = t.FirstLineCell();
        var (_, right) = t.FirstLineRightCell();

        // Tested against the find colours specifically, not "any span background": a changed
        // line already carries the intra-line change marker as a span background, so asking
        // whether anything is tinted answers a different question than the one intended.
        Assert.Contains(((TextElement)left).Spans ?? [], s => IsFindTint(s.Background));
        Assert.DoesNotContain(((TextElement)right).Spans ?? [], s => IsFindTint(s.Background));

        static bool IsFindTint(HultUI.Core.Primitives.Color c) =>
            c == Theme.Nocturne.FindHitBg || c == Theme.Nocturne.FindCurrentBg;
    }

    [Fact]
    public void EnterWalksTheMatchesAcrossBothSides()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "needle\nplain\n");
        t.WriteRight("a.txt", "needle\nneedle\n");
        t.Open();
        t.OpenDiffOn("a.txt");

        Find(t, "needle");
        Assert.Equal(3, Search(t).Hits.Count);
        Assert.Equal(0, Search(t).Index);

        t.Press(KeyCode.Enter);
        Assert.Equal(1, Search(t).Index);
        t.Press(KeyCode.Enter);
        Assert.Equal(2, Search(t).Index);
        t.Press(KeyCode.Enter);
        Assert.Equal(0, Search(t).Index);          // wraps
        t.Press(KeyCode.Enter, KeyModifiers.Shift);
        Assert.Equal(2, Search(t).Index);
    }

    /// <summary>
    /// n and p walk the search while one is running, and the change blocks otherwise. The keys
    /// mean the more specific thing when a query is up: stepping matches is plainly what they
    /// are for then.
    /// </summary>
    [Fact]
    public void NAndPWalkMatchesWhileSearchingAndChangesOtherwise()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "1\n2\n3\n4\n5\n6\n7\n8\n9\n10\n11\n12\n");
        t.WriteRight("a.txt", "1\nX\n3\n4\n5\n6\n7\n8\n9\n10\nY\n12\n");
        t.Open();
        t.OpenDiffOn("a.txt");

        // No search yet: n steps the change blocks, as it always has.
        t.Press(KeyCode.N);
        Assert.Equal(1, t.Field<int>("_diffChangeIdx"));

        Find(t, "1");
        int changeIdx = t.Field<int>("_diffChangeIdx");
        t.Press(KeyCode.Escape);                    // leave the field, keep the bar's matches
        t.Press(KeyCode.N);

        Assert.Equal(changeIdx, t.Field<int>("_diffChangeIdx"));
    }

    [Fact]
    public void TheRegexToggleChangesWhatTheQueryMeans()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "count = 12\n");
        t.WriteRight("a.txt", "count = abc\n");
        t.Open();
        t.OpenDiffOn("a.txt");

        Find(t, @"\d+");
        Assert.Empty(Search(t).Hits);

        t.Press(KeyCode.R, KeyModifiers.Alt);
        Assert.Single(Search(t).Hits);
        Assert.Equal(0, Search(t).Hits[0].Lane);
    }

    [Fact]
    public void EscapeClosesTheBarFirstAndTheViewSecond()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "alpha\n");
        t.WriteRight("a.txt", "beta\n");
        t.Open();
        t.OpenDiffOn("a.txt");
        Find(t, "alpha");

        t.Press(KeyCode.Escape);
        Assert.False(FindOpen(t));
        Assert.True(t.DiffOpen);

        t.Press(KeyCode.Escape);
        Assert.False(t.DiffOpen);
    }

    /// <summary>Body shortcuts stay out of the query. Routing sends the view's keys ahead of
    /// the focused field, so without a guard "w" typed into a search would toggle wrapping.</summary>
    [Fact]
    public void BodyShortcutsDoNotFireWhileTypingInTheField()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "wide\n");
        t.WriteRight("a.txt", "wider\n");
        t.Open();
        t.OpenDiffOn("a.txt");

        bool wrap = t.Wrap;
        t.Press(KeyCode.F, KeyModifiers.Ctrl);
        t.Press(KeyCode.W);

        Assert.Equal(wrap, t.Wrap);
    }

    // ── Reaching the bar's state ─────────────────────────────────────────
    // A private nested type shared with the viewer, so it is read through its public members
    // rather than by naming the type here.

    private static object State(DiffViewTests.Fixture t) => t.Field<object>("_diffFind");

    private static object Member(object owner, string name) =>
        owner.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance)!.GetValue(owner)!;

    private static bool FindOpen(DiffViewTests.Fixture t) => (bool)Member(State(t), "Open");
    private static TextSearch Search(DiffViewTests.Fixture t) => (TextSearch)Member(State(t), "Search");

    /// <summary>Opens the bar and types, the way a user does — real text input to the focused
    /// field, so the app runs whatever it runs on a keystroke.</summary>
    private static void Find(DiffViewTests.Fixture t, string query)
    {
        if (!FindOpen(t)) t.Press(KeyCode.F, KeyModifiers.Ctrl);
        t.Session.Window.TypeText(query);
        t.Session.PumpFrame();
    }
}
