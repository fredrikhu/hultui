using System.Reflection;
using System.Web;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Platform;
using HultUI.Headless;
using Junction.Git;
using Junction.GitLab;
using Xunit;

namespace Junction.Tests.GitLab;

/// <summary>
/// Phase 4, hosted against recordings and a real local origin carrying the merge request's
/// head: the review opens as a base-to-head compare with the threads woven into the diff, a
/// thread card is exactly as tall as the row the virtualised list gives it, the caret and the
/// range are placed from the keyboard and posted with the position GitLab expects, the
/// composer keeps its focus through a rebuild, comments and replies are written as rich text
/// and sent as Markdown, and a review's drafts stay pending until they are submitted together.
/// </summary>
public class ReviewTests
{
    private const string Api = "/api/v4";
    private const string File = "src/api/Orders.cs";
    private const string MrList = $"GET {Api}/projects/7781/merge_requests?state=opened&order_by=updated_at&sort=desc&with_merge_status_recheck=true&per_page=100&page=1";
    private const string Discussions = $"GET {Api}/projects/7781/merge_requests/412/discussions?per_page=100&page=1";
    private const string Drafts = $"GET {Api}/projects/7781/merge_requests/412/draft_notes?per_page=100&page=1";
    private const string Thread1 = "6a9c1e0f3d2b4a5c6d7e8f9012345678abcdef01";

    [Fact]
    public void ALineCodeIsTheShaOfThePathAndBothCounters()
    {
        // sha1("abc") is a9993e36…, the textbook vector.
        Assert.Equal("a9993e364706816aba3e25717850c26c9cd0d89d_12_14", JunctionApp.LineCode("abc", 12, 14));
    }

    [Fact]
    public void APositionCarriesTheSideAndTheRangeGitLabExpects()
    {
        var context = new JunctionApp.ReviewLine(true, 19, 19, "x_19_19", null);
        var added = new JunctionApp.ReviewLine(true, null, 21, "x_19_21", "new");
        var single = JunctionApp.PositionFor("b", "h", "s", File, File, context, context);
        Assert.Equal(19, single.NewLine);
        Assert.Equal(19, single.OldLine);          // a context line is on both sides
        Assert.Null(single.LineRange);

        var range = JunctionApp.PositionFor("b", "h", "s", File, File, context, added);
        Assert.Equal(21, range.NewLine);
        Assert.Null(range.OldLine);                // an added line has no old number
        Assert.Equal("x_19_19", range.LineRange!.Start!.LineCode);
        Assert.Null(range.LineRange.Start.Type);   // unchanged: no type, as GitLab reports it
        Assert.Equal("new", range.LineRange.End!.Type);

        var deleted = new JunctionApp.ReviewLine(false, 30, null, "x_30_31", "old");
        var old = JunctionApp.PositionFor("b", "h", "s", File, File, deleted, deleted);
        Assert.Equal(30, old.OldLine);
        Assert.Null(old.NewLine);
    }

    [Fact]
    public void TheReviewOpensOnTheRequestsRefsAndShowsItsThreads()
    {
        Hosted((repo, origin, session, app) =>
        {
            app.StartReview(repo.Path, 412);
            try { session.PumpUntil(() => app.ReviewReady, TimeSpan.FromSeconds(20)); }
            catch (TimeoutException) { throw new Xunit.Sdk.XunitException("review never became ready: " + app.ReviewState); }
            // The head came down under the remote's namespace, as Checkout locally leaves it.
            Assert.Equal(origin.Head, repo.Git("rev-parse", "refs/remotes/origin/merge-requests/412").Stdout.Trim());

            session.PumpUntil(() => ById(session, "review-thread") is not null, TimeSpan.FromSeconds(20));
            Assert.True(TreeHas(session, "2 threads · 1 unresolved"));
            Assert.NotNull(ById(session, "review-mark"));         // the thread's line carries the mark
            Assert.NotNull(ById(session, "review-plus"));         // every other line the +
            Assert.NotNull(ById(session, "review-legend"));
            Assert.True(TreeHas(session, "Refunds should not count voided tenders."));
            // The tree's pill: Orders.cs has one thread and it is unresolved.
            Assert.NotNull(ById(session, "review-file-pill"));
        });
    }

    /// <summary>A refresh that brings a thread the reader has not seen tags it <i>new</i>
    /// until the card is opened; the threads the review started with never are.</summary>
    [Fact]
    public void AThreadARefreshBringsInIsNewUntilOpened()
    {
        int loads = 0;
        var handler = Routes()
            .Map(Discussions, _ =>
            {
                loads++;
                var json = FakeGitLabHandler.Recording("discussions");
                if (loads > 1)
                    json = "[" + "{\"id\":\"late\",\"individual_note\":false,\"notes\":[{\"id\":7001,\"type\":\"DiffNote\",\"body\":\"Late to the party.\",\"author\":{\"id\":9,\"username\":\"tbrandt\",\"name\":\"Tomas Brandt\"},\"created_at\":\"2026-09-12T12:00:00.000Z\",\"system\":false,\"resolvable\":true,\"resolved\":false,\"position\":{\"position_type\":\"text\",\"old_path\":\"src/api/Orders.cs\",\"new_path\":\"src/api/Orders.cs\",\"new_line\":20}}]},"
                        + json.TrimStart('[');
                return FakeGitLabHandler.Json(json);
            });
        Hosted((repo, _, session, app) =>
        {
            app.StartReview(repo.Path, 412);
            session.PumpUntil(() => ById(session, "review-thread") is not null, TimeSpan.FromSeconds(20));
            Assert.Null(Tag(session, "new"));

            app.RefreshHosted(repo.Path);
            session.PumpUntil(() => TreeHas(session, "Late to the party."), TimeSpan.FromSeconds(20));
            session.PumpFrames(2);
            Assert.NotNull(Tag(session, "new"));

            // Opening it is having seen it.
            var card = LayoutProbe.Descendants<Element>(Root(session)!, e => e.AutomationId == "review-thread").First();
            session.Window.Click(card);
            session.PumpUntil(() => Tag(session, "new") is null, TimeSpan.FromSeconds(10));
        }, handler);

        static TextElement? Tag(HeadlessSession session, string word) =>
            Root(session) is { } root ? LayoutProbe.Descendants<TextElement>(root, t => t.Content == word).FirstOrDefault() : null;
    }

    [Fact]
    public void AThreadCardIsExactlyAsTallAsTheRowTheListGivesIt()
    {
        Hosted((repo, _, session, app) =>
        {
            app.StartReview(repo.Path, 412);
            session.PumpUntil(() => ById(session, "review-thread") is not null, TimeSpan.FromSeconds(20));
            session.PumpFrames(2);

            // Collapsed first, then opened: both heights are measured, both must hold.
            AssertCardFitsItsRow(session);
            session.Window.Click(ById(session, "review-thread")!);
            session.PumpUntil(() => TreeHas(session, "Good catch, fixed in the next push."), TimeSpan.FromSeconds(10));
            session.PumpFrames(2);
            Assert.True(TreeHas(session, "Thread on lines 23–25 (new)"));
            AssertCardFitsItsRow(session);
        });
    }

    /// <summary>A note written in markdown — a heading, a paragraph long and bold enough that
    /// its weight changes its height, nested and task lists, a quote and a fenced block — is
    /// measured the way it is drawn, so its card still fits the row it was given. Measured
    /// without its spans the same card comes out ~4px taller than its row.</summary>
    [Fact]
    public void AMarkdownNoteIsMeasuredTheWayItIsDrawn()
    {
        var bold = string.Join(" ", Enumerable.Repeat("**a refund that splits across tenders must skip the voided ones**", 120));
        var note = string.Join("\n", [
            "## Voided tenders",
            "",
            bold + " and _then_ `Refunds()` counts what is left.",
            "",
            "- one item",
            "- two",
            "  1. nested",
            "- [x] checked",
            "",
            "> quoted reply",
            "",
            "```csharp",
            "var refunds = tenders.Where(t => !t.Voided);",
            "```",
        ]);
        var handler = Routes().Map(Discussions, _ => FakeGitLabHandler.Json(FakeGitLabHandler.Recording("discussions")
            .Replace("\"Refunds should not count voided tenders.\"", System.Text.Json.JsonSerializer.Serialize(note))));
        Hosted((repo, _, session, app) =>
        {
            app.StartReview(repo.Path, 412);
            session.PumpUntil(() => ById(session, "review-thread") is not null, TimeSpan.FromSeconds(20));
            session.PumpFrames(2);
            AssertCardFitsItsRow(session);

            session.Window.Click(ById(session, "review-thread")!);
            session.PumpUntil(() => TreeHas(session, "Good catch, fixed in the next push."), TimeSpan.FromSeconds(10));
            session.PumpFrames(2);
            Assert.True(TreeHas(session, "var refunds = tenders"));    // the fence's code…
            Assert.False(TreeHas(session, "```"));                      // …without the fence
            Assert.False(TreeHas(session, "## "));
            var paragraph = LayoutProbe.Descendants<TextElement>(Root(session)!, t => t.Content.StartsWith("a refund that splits")).Single();
            Assert.Contains(paragraph.Spans!, s => s.Weight == HultUI.Core.Styling.FontWeight.SemiBold);
            Assert.True(LayoutProbe.Painted(paragraph).Height > 40f, "the bold paragraph wraps");
            AssertCardFitsItsRow(session);
        }, handler);
    }

    /// <summary>
    /// A card whose height changes after it was measured: its picture arrives a frame after
    /// the card opens, and its section opens on a click. Each time the row is measured again,
    /// so the card fits it throughout. Without the layout generation in the row signature the
    /// picture lands in a row still sized for its "loading" line.
    /// </summary>
    [Fact]
    public void ACardFitsItsRowAsItsPictureArrivesAndItsSectionOpens()
    {
        var note = string.Join("\n", [
            "Before and after:",
            "",
            "![shot](/uploads/0a1b2c3d/shot.png)",
            "",
            "| Tender | Note |",
            "|--------|------|",
            "| card | " + string.Join(" ", Enumerable.Repeat("a cell long enough to wrap inside its share of the card", 6)) + " |",
            "",
            "<details><summary>Logs</summary>",
            "",
            "hidden line one",
            "",
            "hidden line two",
            "",
            "</details>",
        ]);
        var handler = Routes()
            .Map(Discussions, _ => FakeGitLabHandler.Json(FakeGitLabHandler.Recording("discussions")
                .Replace("\"Refunds should not count voided tenders.\"", System.Text.Json.JsonSerializer.Serialize(note))))
            .Map($"GET {Api}/projects/7781/uploads/0a1b2c3d/shot.png", _ => FakeGitLabHandler.Bytes(FakeGitLabHandler.Png(800, 400)));
        Hosted((repo, _, session, app) =>
        {
            app.StartReview(repo.Path, 412);
            session.PumpUntil(() => ById(session, "review-thread") is not null, TimeSpan.FromSeconds(20));
            session.PumpFrames(2);
            session.Window.Click(ById(session, "review-thread")!);
            session.PumpUntil(() => ById(session, "md-image") is not null, TimeSpan.FromSeconds(10));
            session.PumpFrames(2);
            // Wider than the card's text column, so scaled down to it with its ratio kept.
            var picture = LayoutProbe.Painted(ById(session, "md-image")!);
            Assert.True(picture.Width < 800f, $"picture {picture.Width} wide");
            Assert.Equal(MathF.Floor(picture.Width / 2), picture.Height);
            AssertCardFitsItsRow(session);

            Assert.False(TreeHas(session, "hidden line one"));
            // The picture pushed the section's summary below the window: scroll to it.
            for (int i = 0; i < 20 && LayoutProbe.Painted(ById(session, "md-details")!).Bottom > 860; i++)
            {
                session.Window.Wheel(900, 500, 0, -3);
                session.PumpFrames(2);
            }
            session.Window.Click(ById(session, "md-details")!);
            session.PumpFrames(2);
            Assert.True(TreeHas(session, "hidden line two"));
            AssertCardFitsItsRow(session);
        }, handler);
    }

    private static void AssertCardFitsItsRow(HeadlessSession session) => AssertCardFitsItsRow(ById(session, "review-thread")!);

    /// <summary>The same for the card row <paramref name="inside"/> is, or is in: a thread's
    /// or the composer's.</summary>
    private static void AssertCardFitsItsRow(Element inside)
    {
        var outer = inside;
        while (outer.AutomationId is not ("review-thread" or "review-composer")) outer = outer.Parent!;
        var card = outer.Children[0];
        float row = LayoutProbe.Painted(outer).Height;
        float inner = LayoutProbe.Painted(card).Height;
        // The row is the card plus its 4px top and 8px bottom margin, to the pixel — a row
        // shorter than the card clips it, a taller one leaves a gap under it.
        Assert.True(Math.Abs(row - 12f - inner) <= 1f, $"row {row} vs card {inner}");
        Assert.True(inner > 30f, "the card has content");
    }

    [Fact]
    public void ArrowsPlaceTheCaretShiftExtendsTheRangeAndTheCommentIsPostedWhereGitLabExpectsIt()
    {
        var handler = Routes();
        Hosted((repo, _, session, app) =>
        {
            app.StartReview(repo.Path, 412);
            session.PumpUntil(() => ById(session, "review-plus") is not null, TimeSpan.FromSeconds(20));
            session.PumpFrames(2);

            // Down lands on the first line (the hunk header is skipped); Shift+Down twice
            // extends over three context lines, 19–21 new.
            session.Window.PressKey(KeyCode.Down);
            session.PumpFrames(2);
            Assert.Equal(1, app.ReviewCaret);
            Assert.NotNull(ById(session, "review-caret-row"));
            session.Window.PressKey(KeyCode.Down, KeyModifiers.Shift);
            session.Window.PressKey(KeyCode.Down, KeyModifiers.Shift);
            session.PumpFrames(2);
            Assert.Equal((1, 3), app.ReviewRange);

            session.Window.PressKey(KeyCode.C);
            session.PumpUntil(() => ById(session, "review-composer") is not null, TimeSpan.FromSeconds(10));
            session.PumpUntil(() => Focused(session) is RichTextEditorElement { AutomationId: "review-composer-text" }, TimeSpan.FromSeconds(10));
            Assert.True(TreeHas(session, "New comment on lines 19–21 (new)"));
            var editor = (RichTextEditorElement)Focused(session)!;
            AssertCardFitsItsRow(editor);

            // Written as rich text: Markdown's own shortcut makes the word bold.
            session.Window.TypeText("Looks **wrong**");
            session.PumpFrames(2);
            Assert.Equal("Looks wrong", editor.Document.ToPlainText());

            // A rebuild of the whole tree — what every hosted refresh causes — leaves the
            // same editor focused with its text: the keyed row was kept, not replaced.
            app.RefreshHosted(repo.Path);
            session.PumpFrames(4);
            Assert.Same(editor, Focused(session));
            Assert.Equal("Looks wrong", editor.Document.ToPlainText());

            session.Window.PressKey(KeyCode.Enter, KeyModifiers.Ctrl);
            session.PumpUntil(() => handler.Seen.Any(s => s.Request.Method == System.Net.Http.HttpMethod.Post), TimeSpan.FromSeconds(10));
            var post = handler.Seen.Single(s => s.Request.Method == System.Net.Http.HttpMethod.Post);
            Assert.Equal($"{Api}/projects/7781/merge_requests/412/discussions", post.Request.RequestUri!.AbsolutePath);
            var form = HttpUtility.ParseQueryString(post.Body!);
            Assert.Equal("Looks **wrong**", form["body"]);   // the Markdown GitLab reads
            Assert.Equal("text", form["position[position_type]"]);
            Assert.Equal(repo.Git("rev-parse", "HEAD").Stdout.Trim(), form["position[base_sha]"]);
            Assert.Equal(File, form["position[new_path]"]);
            Assert.Equal("21", form["position[new_line]"]);
            Assert.Equal("21", form["position[old_line]"]);
            Assert.Equal(JunctionApp.LineCode(File, 19, 19), form["position[line_range][start][line_code]"]);
            Assert.Equal(JunctionApp.LineCode(File, 21, 21), form["position[line_range][end][line_code]"]);
            Assert.Equal("19", form["position[line_range][start][new_line]"]);
            Assert.Equal("21", form["position[line_range][end][old_line]"]);
            Assert.Null(form["position[line_range][start][type]"]);   // context: no side

            // Posted: the composer is gone and the range cleared.
            session.PumpUntil(() => ById(session, "review-composer") is null, TimeSpan.FromSeconds(10));
            Assert.Null(app.ReviewRange);
        }, handler);
    }

    /// <summary>The rows hear the pointer only while a range is dragged (plan 17, round 4: their
    /// move handler is passive and listens only then). A move along an idle row draws no frame;
    /// a drag from the + still grows the range over every row it crosses, and the release opens
    /// the composer.</summary>
    [Fact]
    public void DraggingFromThePlusGrowsTheRangeOverTheRowsCrossed_AndAnIdleMoveDrawsNoFrame()
    {
        Hosted((repo, _, session, app) =>
        {
            app.StartReview(repo.Path, 412);
            session.PumpUntil(() => ById(session, "review-plus") is not null, TimeSpan.FromSeconds(20));
            session.PumpFrames(2);

            var plus = ById(session, "review-plus")!;
            var box = plus.WindowBorderBox;
            float x = box.X + box.Width / 2, y = box.Y + box.Height / 2;
            float rowHeight = plus.Parent!.Parent!.WindowBorderBox.Height;

            // Along the code of a line, nothing pressed: nothing changes, so nothing is drawn.
            session.Window.MoveMouse(x + 200, y);
            session.PumpFrames(2);
            session.Window.MoveMouse(x + 203, y);
            Assert.False(session.PumpFrame(), "a move along an idle review row drew a frame");

            session.Window.MoveMouse(x, y);
            session.PumpFrame();
            session.Window.MouseDown(x, y);
            session.PumpFrame();
            int first = app.ReviewCaret;
            Assert.True(first >= 0, "the press on the + placed no caret");
            session.Window.MoveMouse(x, y + rowHeight);
            session.PumpFrame();
            session.Window.MoveMouse(x, y + 2 * rowHeight);
            session.PumpFrame();
            Assert.Equal((first, first + 2), app.ReviewRange);

            session.Window.MouseUp(x, y + 2 * rowHeight);
            session.PumpUntil(() => ById(session, "review-composer") is not null, TimeSpan.FromSeconds(10));
            Assert.True(TreeHas(session, "New comment on lines 19–21 (new)"));
        });
    }

    [Fact]
    public void ARangeStopsWhereTheSideChanges()
    {
        Hosted((repo, _, session, app) =>
        {
            app.StartReview(repo.Path, 412);
            session.PumpUntil(() => ById(session, "review-plus") is not null, TimeSpan.FromSeconds(20));
            session.PumpFrames(2);
            // Lines 19–21 are context, 22–26 old deletions then new additions. From line 21
            // (new side) Shift+Down cannot cross into the deletions.
            session.Window.PressKey(KeyCode.Down);
            session.Window.PressKey(KeyCode.Down);
            session.Window.PressKey(KeyCode.Down);
            session.Window.PressKey(KeyCode.Down, KeyModifiers.Shift);
            session.PumpFrames(2);
            Assert.Equal((3, 3), app.ReviewRange);
            session.Window.PressKey(KeyCode.C);
            session.PumpUntil(() => ById(session, "review-composer") is not null, TimeSpan.FromSeconds(10));
            Assert.True(TreeHas(session, "range stops at the side boundary"));
        });
    }

    [Fact]
    public void StartReviewKeepsCommentsAsDraftsUntilTheyAreSubmittedTogether()
    {
        var drafts = new List<string>();
        var handler = Routes(drafts);
        Hosted((repo, _, session, app) =>
        {
            app.StartReview(repo.Path, 412);
            session.PumpUntil(() => ById(session, "review-plus") is not null, TimeSpan.FromSeconds(20));
            session.PumpFrames(2);
            session.Window.PressKey(KeyCode.Down);
            session.Window.PressKey(KeyCode.C);
            session.PumpUntil(() => Focused(session) is RichTextEditorElement { AutomationId: "review-composer-text" }, TimeSpan.FromSeconds(10));
            session.Window.TypeText("Keep for later");
            session.PumpFrames(2);

            session.Window.Click(Named(session, "Start review"));
            session.PumpUntil(() => handler.Seen.Any(s => s.Request.Method == System.Net.Http.HttpMethod.Post), TimeSpan.FromSeconds(10));
            var post = handler.Seen.Single(s => s.Request.Method == System.Net.Http.HttpMethod.Post);
            Assert.Equal($"{Api}/projects/7781/merge_requests/412/draft_notes", post.Request.RequestUri!.AbsolutePath);
            Assert.Equal("Keep for later", HttpUtility.ParseQueryString(post.Body!)["note"]);

            // The strip counts the draft, the header says the review is on, the card is pending.
            session.PumpUntil(() => TreeHas(session, "1 comment pending"), TimeSpan.FromSeconds(10));
            Assert.NotNull(ById(session, "review-pending"));
            Assert.True(TreeHas(session, "Reviewing · 1 pending"));
            Assert.True(TreeHas(session, "not published yet") || TreeHas(session, "pending"));

            app.SubmitOpenReview(null);
            session.PumpUntil(() => handler.Seen.Any(s => s.Request.RequestUri!.AbsolutePath.EndsWith("/bulk_publish")), TimeSpan.FromSeconds(10));
            session.PumpUntil(() => ById(session, "review-pending") is null, TimeSpan.FromSeconds(10));
            // The reload lands after the submit; the drafts it no longer carries must not
            // resurrect the review from the value loaded before it.
            session.PumpUntil(() => !TreeHas(session, "Reviewing ·"), TimeSpan.FromSeconds(10));
            session.PumpFrames(5);
            Assert.False(TreeHas(session, "Reviewing ·"));
            Assert.Null(ById(session, "review-pending"));
        }, handler);
    }

    [Fact]
    public void AReplyAndAResolveGoToTheThread()
    {
        var handler = Routes();
        Hosted((repo, _, session, app) =>
        {
            app.StartReview(repo.Path, 412, focusThread: Thread1);
            session.PumpUntil(() => ById(session, "review-reply") is not null, TimeSpan.FromSeconds(20));
            session.PumpFrames(2);

            // The field opens an editor in the card, focused, and the card grows to fit it.
            // Esc drops it, and leaves the review where it was.
            session.Window.Click(ById(session, "review-reply")!);
            session.PumpUntil(() => Focused(session) is RichTextEditorElement { AutomationId: "review-reply-text" }, TimeSpan.FromSeconds(10));
            session.PumpFrames(2);
            AssertCardFitsItsRow(Focused(session)!);
            session.Window.PressKey(KeyCode.Escape);
            session.PumpFrames(2);
            Assert.Null(ById(session, "review-reply-text"));
            Assert.NotNull(ById(session, "review-reply"));
            Assert.True(app.ReviewOpen);
            AssertCardFitsItsRow(ById(session, "review-reply")!);

            // Written as rich text, sent as Markdown: the backticks make code.
            session.Window.Click(ById(session, "review-reply")!);
            session.PumpUntil(() => Focused(session) is RichTextEditorElement { AutomationId: "review-reply-text" }, TimeSpan.FromSeconds(10));
            session.Window.TypeText("Agreed, with `Refunds()`");
            session.PumpFrames(2);
            Assert.Equal("Agreed, with Refunds()", ((RichTextEditorElement)Focused(session)!).Document.ToPlainText());
            session.Window.PressKey(KeyCode.Enter, KeyModifiers.Ctrl);
            session.PumpUntil(() => handler.Seen.Any(s => s.Request.RequestUri!.AbsolutePath.EndsWith($"/discussions/{Thread1}/notes")), TimeSpan.FromSeconds(10));
            var post = handler.Seen.Single(s => s.Request.RequestUri!.AbsolutePath.EndsWith($"/discussions/{Thread1}/notes"));
            Assert.Equal("Agreed, with `Refunds()`", HttpUtility.ParseQueryString(post.Body!)["body"]);

            // Posted: the editor closes back into the field.
            session.PumpUntil(() => ById(session, "review-reply") is not null, TimeSpan.FromSeconds(10));
            Assert.Null(ById(session, "review-reply-text"));
            session.PumpFrames(2);
            session.Window.Click(Named(session, "Resolve"));
            session.PumpUntil(() => handler.Seen.Any(s => s.Request.Method == System.Net.Http.HttpMethod.Put), TimeSpan.FromSeconds(10));
            var put = handler.Seen.Single(s => s.Request.Method == System.Net.Http.HttpMethod.Put);
            Assert.Equal($"{Api}/projects/7781/merge_requests/412/discussions/{Thread1}", put.Request.RequestUri!.AbsolutePath);
            Assert.Equal("true", HttpUtility.ParseQueryString(put.Body!)["resolved"]);
        }, handler);
    }

    /// <summary>A caret or range move keeps the woven rows: the list makes again only the rows
    /// whose caret or range marks changed — two per key — and keeps every other row and its
    /// label. Anything else (here: expanding the threads) still makes them all again.</summary>
    [Fact]
    public void ACaretMove_MakesTheRowsItMovedBetween_AndKeepsTheRest()
    {
        Hosted((repo, _, session, app) =>
        {
            app.StartReview(repo.Path, 412);
            session.PumpUntil(() => ById(session, "review-plus") is not null, TimeSpan.FromSeconds(20));
            session.PumpFrames(4);
            session.Window.PressKey(KeyCode.Down);
            session.PumpFrames(2);
            Assert.Equal(1, app.ReviewCaret);

            // Lines 1-3 are context (19-21, new side), 4 on are deletions: two Shift+Down band the
            // three, then a plain move clears the band onto line 4, and another moves on.
            void Step(KeyModifiers mods, int expected, string what)
            {
                var before = Rows(session);
                var caretBefore = ById(session, "review-caret-row");
                var label = Label(session);
                int made = app.DiffRowsMade;

                session.Window.PressKey(KeyCode.Down, mods);
                session.PumpFrames(2);

                Assert.True(app.DiffRowsMade - made == expected,
                    $"{what}: {app.DiffRowsMade - made} rows made, caret {app.ReviewCaret}, range {app.ReviewRange}");
                var after = Rows(session);
                Assert.Equal(before.Count, after.Count);
                Assert.Equal(before.Count - expected, before.Zip(after).Count(p => ReferenceEquals(p.First, p.Second)));
                var caret = ById(session, "review-caret-row");
                Assert.NotNull(caret);
                Assert.NotSame(caretBefore, caret);
                Assert.Same(label, Label(session));
            }

            Step(KeyModifiers.Shift, 2, "Shift+Down from line 1");
            Step(KeyModifiers.Shift, 2, "Shift+Down from line 2");
            Assert.Equal((1, 3), app.ReviewRange);
            Assert.Equal(3, Rows(session).Count(r => r is BoxElement { BackgroundColor: var c } && c == Junction.Theme.Nocturne.Accent900));
            Step(KeyModifiers.None, 4, "Down out of the band");
            Assert.Null(app.ReviewRange);
            Assert.Equal(0, Rows(session).Count(r => r is BoxElement { BackgroundColor: var c } && c == Junction.Theme.Nocturne.Accent900));
            Step(KeyModifiers.None, 2, "Down");
            Assert.Equal(5, app.ReviewCaret);

            // Expanding the threads changes what the cards show: the rows are made again.
            int beforeExpand = app.DiffRowsMade;
            session.Window.PressKey(KeyCode.E);
            session.PumpFrames(2);
            Assert.True(app.DiffRowsMade - beforeExpand > 10, $"{app.DiffRowsMade - beforeExpand} rows made after expanding");
        });
    }

    /// <summary>Typing in a comment — the composer's or a reply's — builds the window once a
    /// character (the editor tells an edit and the caret's move in one event), leaves the chrome
    /// as it is and keeps the woven rows: the list makes again only the card holding the editor,
    /// whose formatting bar follows the caret. It made every row twice a character and built the
    /// chrome three times (1.4 MB a character on a 400-line file).</summary>
    [Fact]
    public void TypingAComment_BuildsOnceACharacter_AndMakesOnlyItsCardAgain()
    {
        Hosted((repo, _, session, app) =>
        {
            app.StartReview(repo.Path, 412, focusThread: Thread1);
            session.PumpUntil(() => ById(session, "review-plus") is not null && ById(session, "review-reply") is not null,
                TimeSpan.FromSeconds(20));
            session.PumpFrames(4);

            void TypeAndCheck(string text, string card)
            {
                int quiet = 0;
                foreach (var ch in text)
                {
                    var before = Rows(session);
                    var label = Label(session);
                    int builds = app.Builds, chrome = app.ChromeBuilds, made = app.DiffRowsMade;
                    session.Window.TypeText(ch.ToString());
                    // The character's own rebuild runs as its event ends: one, and not the chrome.
                    Assert.Equal(1, app.Builds - builds);
                    Assert.Equal(0, app.ChromeBuilds - chrome);
                    session.PumpFrames(2);
                    // Something outside the typing — the repository's watcher seeing the review's
                    // fetch — may rebuild the window meanwhile, and that weaves again. Otherwise the
                    // frame makes the card again and nothing else: it is keyed, so it is made into
                    // the row it had, every row on screen is the element it was, and every label
                    // the list kept is kept.
                    if (app.Builds - builds > 1) continue;
                    quiet++;
                    Assert.Equal(1, app.DiffRowsMade - made);
                    Assert.Equal(before, Rows(session));
                    Assert.Same(label, Label(session));
                }
                Assert.True(quiet >= text.Length - 2, $"{quiet} of {text.Length} characters typed without a rebuild from elsewhere");
                Assert.Contains(text.Replace("*", ""), ((RichTextEditorElement)Focused(session)!).Document.ToPlainText());
                Assert.NotNull(ById(session, card));
            }

            // A reply's editor is in its thread's card, and that card is the one made again.
            session.Window.Click(ById(session, "review-reply")!);
            session.PumpUntil(() => Focused(session) is RichTextEditorElement { AutomationId: "review-reply-text" }, TimeSpan.FromSeconds(10));
            session.PumpFrames(4);
            TypeAndCheck("Agreed", "review-reply-text");

            // The composer, on the caret's line.
            session.Window.Click(ById(session, "review-plus")!);
            session.PumpFrames(2);
            session.Window.PressKey(KeyCode.C);
            session.PumpUntil(() => Focused(session) is RichTextEditorElement { AutomationId: "review-composer-text" }, TimeSpan.FromSeconds(10));
            session.PumpFrames(4);
            TypeAndCheck("Not **this**", "review-composer");

            // The caret moving into the bold word lights the card's Bold button: the bar is in the
            // card made again, and it reads the editor on screen.
            for (int i = 0; i < 2; i++)
            {
                int builds = app.Builds, chrome = app.ChromeBuilds;
                session.Window.PressKey(KeyCode.Left);
                Assert.Equal(1, app.Builds - builds);
                Assert.Equal(0, app.ChromeBuilds - chrome);
                session.PumpFrames(2);
            }
            var bold = LayoutProbe.Descendants<ButtonElement>(ById(session, "review-composer")!, b => b.AutomationId == "richtext-bold").Single();
            Assert.True(bold.AccessibleToggled);
        });
    }

    /// <summary>The diff list's realized rows, top to bottom (its first child is the spacer).</summary>
    private static List<Element> Rows(HeadlessSession session) =>
        Root(session) is { } root
            ? LayoutProbe.Descendants<Element>(root, e => e.Key == "diff:" + File).Single().Children.Skip(1).ToList()
            : [];

    /// <summary>The diff list's item label delegate.</summary>
    private static object? Label(HeadlessSession session)
    {
        var list = LayoutProbe.Descendants<Element>(Root(session)!, e => e.Key == "diff:" + File).Single();
        return list.GetType().GetProperty("AccessibleLabel")!.GetValue(list);
    }

    // ── Hosting ──────────────────────────────────────────────────────────────────────

    private static string MrJson(string baseSha, string headSha) =>
        "{\"id\":90412,\"iid\":412,\"project_id\":7781,\"title\":\"PC-1042: refund splitting\",\"state\":\"opened\"," +
        "\"source_branch\":\"feature/2.4.0/payments/PC-1042-refund-splitting\",\"target_branch\":\"main\"," +
        $"\"sha\":\"{headSha}\",\"web_url\":\"https://gitlab.com/acme/core-api/-/merge_requests/412\"," +
        "\"author\":{\"id\":42,\"username\":\"dosei\",\"name\":\"Dana Osei\"},\"updated_at\":\"2026-09-12T09:44:00.000Z\"," +
        $"\"diff_refs\":{{\"base_sha\":\"{baseSha}\",\"head_sha\":\"{headSha}\",\"start_sha\":\"{baseSha}\"}}}}";

    private static FakeGitLabHandler Routes(List<string>? drafts = null)
    {
        drafts ??= [];
        var h = new FakeGitLabHandler()
            .Map($"GET {Api}/projects/acme%2Fcore-api", "project")
            .Map($"GET {Api}/projects/7781/protected_branches?per_page=100&page=1", "protected_branches")
            .Map(MrList, "merge_requests_page1")
            .Map($"GET {Api}/projects/7781/merge_requests/412/approvals", "approvals")
            .Map(Discussions, "discussions")
            .MapJson($"GET {Api}/projects/7781/merge_requests/412/diffs?per_page=100&page=1",
                "[{\"old_path\":\"src/api/Orders.cs\",\"new_path\":\"src/api/Orders.cs\",\"new_file\":false,\"renamed_file\":false,\"deleted_file\":false}]")
            .MapJson($"GET {Api}/projects/7781/pipelines?order_by=updated_at&sort=desc&per_page=100&page=1", "[]")
            .Map(Drafts, _ => FakeGitLabHandler.Json("[" + string.Join(",", drafts) + "]"))
            .MapJson($"POST {Api}/projects/7781/merge_requests/412/discussions",
                "{\"id\":\"newthread\",\"individual_note\":false,\"notes\":[{\"id\":9001,\"type\":\"DiffNote\",\"body\":\"Looks wrong\",\"system\":false,\"resolvable\":true,\"resolved\":false}]}", 201)
            .Map($"POST {Api}/projects/7781/merge_requests/412/draft_notes", req =>
            {
                var body = req.Content!.ReadAsStringAsync().Result;
                var note = HttpUtility.ParseQueryString(body)["note"] ?? "";
                var json = "{\"id\":77,\"author_id\":42,\"merge_request_id\":90412,\"note\":\"" + note + "\",\"position\":{\"position_type\":\"text\"," +
                    "\"new_path\":\"src/api/Orders.cs\",\"old_path\":\"src/api/Orders.cs\",\"new_line\":19,\"old_line\":19}}";
                drafts.Add(json);
                return FakeGitLabHandler.Json(json, 201);
            })
            .Map($"POST {Api}/projects/7781/merge_requests/412/draft_notes/bulk_publish", _ =>
            {
                drafts.Clear();
                return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.NoContent);
            })
            .MapJson($"POST {Api}/projects/7781/merge_requests/412/discussions/{Thread1}/notes",
                "{\"id\":9002,\"type\":\"DiscussionNote\",\"body\":\"Agreed\",\"system\":false,\"resolvable\":true,\"resolved\":false}", 201)
            .MapJson($"PUT {Api}/projects/7781/merge_requests/412/discussions/{Thread1}",
                "{\"id\":\"" + Thread1 + "\",\"individual_note\":false,\"notes\":[]}");
        return h;
    }

    /// <summary>A local clone whose origin carries <c>refs/merge-requests/412/head</c>: the
    /// base commit writes forty lines of Orders.cs, the head rewrites lines 22–26, so the one
    /// hunk spans new lines 19–29 and the recorded thread on 23–25 has a line to sit on.</summary>
    private static void Hosted(Action<TempRepo, TempRepo, HeadlessSession, JunctionApp> body, FakeGitLabHandler? handler = null)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Write(File, string.Join("", Enumerable.Range(1, 40).Select(i => $"line {i};\n")));
        repo.Git("add", "-A");
        repo.Git("commit", "-q", "-m", "orders");
        var baseSha = repo.Head;

        // The remote is gitlab.com as far as detection is concerned, and the local origin as
        // far as git is concerned: the same insteadOf a corporate mirror would use.
        using var origin = TempRepo.CreateEmpty();
        origin.Git("config", "receive.denyCurrentBranch", "ignore");
        repo.Git("remote", "add", "origin", "https://gitlab.com/acme/core-api.git");
        repo.Git("config", $"url.{origin.Path.Replace('\\', '/')}.insteadOf", "https://gitlab.com/acme/core-api.git");
        repo.Git("push", "-q", "origin", "main:main");

        repo.Git("checkout", "-q", "-b", "feature/2.4.0/payments/PC-1042-refund-splitting");
        repo.Write(File, string.Join("", Enumerable.Range(1, 40).Select(i => i is >= 22 and <= 26 ? $"changed {i};\n" : $"line {i};\n")));
        repo.Git("commit", "-q", "-am", "rewrite the refund split");
        var headSha = repo.Head;
        repo.Git("push", "-q", "origin", "HEAD:refs/merge-requests/412/head");
        origin.Git("update-ref", "HEAD", headSha);
        repo.Git("checkout", "-q", "main");
        repo.Git("branch", "-q", "-D", "feature/2.4.0/payments/PC-1042-refund-splitting");

        handler ??= Routes();
        handler.MapJson($"GET {Api}/projects/7781/merge_requests/412", MrJson(baseSha, headSha));

        var store = new InMemoryCredentialStore();
        store.Write(GitLabAccounts.Service("gitlab.com"), GitLabAccounts.TokenAccount, "glpat-test");
        var settings = new JunctionSettingsData
        {
            RepositoryPaths = [repo.Path],
            ActiveRepoPath = repo.Path,
            Workspaces = [new WorkspaceData { Id = "w1", Name = "Tests", RepoPaths = [repo.Path] }],
            ActiveWorkspaceId = "w1",
        };
        settings.GitLabAccounts.Add(new GitLabAccountData
        {
            Host = "gitlab.com", WebBaseUrl = "https://gitlab.com", UserId = 42, Username = "dosei", Name = "Dana Osei",
            Kind = GitLabTokenKind.Pat, Scopes = ["api"], ExpiresAt = DateTimeOffset.UtcNow.AddDays(200),
        });

        using var session = HeadlessSession.Create(1440, 900);
        JunctionApp? app = null;
        try
        {
            app = new JunctionApp(session.Application, session.Window.Window, settings,
                credentialStore: store, gitLabHandler: _ => handler);
            session.Application.SetRoot(app.Mount());
            session.PumpUntil(() => TreeHas(session, "orders"), TimeSpan.FromSeconds(15));
            body(repo, origin, session, app);
        }
        finally { app?.Dispose(); }
    }

    private static Element? Root(HeadlessSession session) =>
        (Element?)typeof(HultUI.Window)
            .GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Window.Window);

    private static Element? Focused(HeadlessSession session) => session.Window.Window.FocusedElement;

    private static bool TreeHas(HeadlessSession session, string text) =>
        Root(session) is { } root &&
        LayoutProbe.Descendants<TextElement>(root, t => t.Content.Contains(text, StringComparison.Ordinal)).Count > 0;

    private static Element? ById(HeadlessSession session, string id) =>
        Root(session) is { } root
            ? LayoutProbe.Descendants<Element>(root, e => e.AutomationId == id).FirstOrDefault()
            : null;

    private static Element Named(HeadlessSession session, string name)
    {
        var el = Root(session) is { } root
            ? LayoutProbe.Descendants<Element>(root, e => e.AccessibleName == name).FirstOrDefault()
            : null;
        return el ?? throw new Xunit.Sdk.XunitException($"nothing on screen is named \"{name}\"");
    }
}
