using System.Collections.Immutable;
using System.Reflection;
using HultUI.Core.Elements;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace HultUI.SourceGenerators.Tests;

/// <summary>
/// HULTUI002 exists to catch one specific silent failure: a style assigned on an element
/// that has already been laid out, with nothing to invalidate it. Half of these tests
/// are the shapes it must catch; the other half are the shapes it must stay quiet on,
/// which matter more — the three apps contain ~170 correct construction-time style
/// assignments, and a rule that flagged those would be worse than no rule.
/// </summary>
public class StyleMutationAnalyzerTests
{
    // ──── Shapes that must be reported ────

    [Fact]
    public void CapturedElement_MutatedInHandler_IsReported()
    {
        // The split-pane drag that the rule is calibrated on: the panes never moved,
        // and nothing said why.
        var diagnostics = Analyze("""
            var handle = Box.New();
            var pane = Box.New();
            Register(() => { pane.Flex.FlexBasis = Dimension.Px(200); });
            """);

        var d = Assert.Single(diagnostics);
        Assert.Equal("HULTUI002", d.Id);
        Assert.Contains("FlexStyle.FlexBasis", d.GetMessage());
        Assert.Contains("MarkLayoutDirty", d.GetMessage());
    }

    [Fact]
    public void EventArgumentElement_IsReported()
    {
        // e.CurrentTarget names an element built long before this handler runs.
        Assert.Single(Analyze("""
            Register(e => { e.Target.Box.Width = Dimension.Px(10); });
            """));
    }

    [Fact]
    public void FieldElement_MutatedInHandler_IsReported()
    {
        Assert.Single(AnalyzeInClass("""
            private readonly BoxElement _pane = Box.New();
            public void Wire() => Register(() => { _pane.Box.Height = Dimension.Px(10); });
            """));
    }

    [Fact]
    public void ZIndex_AsksForPaintDirty_NotLayout()
    {
        // Stacking order changes what is drawn on top, not where anything sits.
        var d = Assert.Single(Analyze("""
            var el = Box.New();
            Register(() => { el.Position.ZIndex = 5; });
            """));

        Assert.Contains("MarkPaintDirty", d.GetMessage());
        Assert.DoesNotContain("MarkLayoutDirty", d.GetMessage());
    }

    [Fact]
    public void InheritedFontSize_AsksForBothPasses()
    {
        // Re-resolving alone leaves the text drawn at the size it was last measured at.
        var d = Assert.Single(Analyze("""
            var el = Text.New("hi");
            Register(() => { el.Inherited.FontSize = 20f; });
            """));

        Assert.Contains("InheritedStyle.FontSize", d.GetMessage());
        Assert.Contains("MarkStyleDirty() and MarkLayoutDirty()", d.GetMessage());
    }

    [Fact]
    public void InheritedTextColor_AsksForTheStylePassOnly()
    {
        // Colour does not move anything, so re-resolving is the whole fix.
        var d = Assert.Single(Analyze("""
            var el = Text.New("hi");
            Register(() => { el.Inherited.TextColor = Color.Red; });
            """));

        Assert.Contains("MarkStyleDirty()", d.GetMessage());
        Assert.DoesNotContain("MarkLayoutDirty", d.GetMessage());
    }

    [Fact]
    public void InheritedStyle_FollowedByMarkStyleDirty_IsNotReported()
    {
        Assert.Empty(Analyze("""
            var el = Text.New("hi");
            Register(() =>
            {
                el.Inherited.TextColor = Color.Red;
                el.MarkStyleDirty();
            });
            """));
    }

    // ──── Shapes that must stay quiet ────

    [Fact]
    public void ConstructionTimeMutation_IsNotReported()
    {
        // A fresh element starts fully dirty, so this is correct and extremely common:
        // every modal backdrop in Junction is written exactly this way.
        Assert.Empty(Analyze("""
            var backdrop = Box.New();
            backdrop.Position.Type = PositionType.Absolute;
            backdrop.Position.Top = Dimension.Px(0);
            """));
    }

    [Fact]
    public void ElementBuiltInsideTheLambda_IsNotReported()
    {
        // Construction code that merely happens to live in a callback.
        Assert.Empty(Analyze("""
            Register(() =>
            {
                var popup = Box.New();
                popup.Position.Type = PositionType.Absolute;
            });
            """));
    }

    [Fact]
    public void ConfigurationCallbackSubject_IsNotReported()
    {
        // AnimatedBox(key, color, box => …) in the Animation sample: the element arrives
        // fresh from the factory for this callback to set up. Distinguished from the
        // e.Target case above by being the parameter itself rather than a member of it.
        Assert.Empty(AnalyzeInClass("""
            public void Wire() => Configure(box => { box.Box.Width = Dimension.Px(0); });
            private static void Configure(Action<BoxElement> setup) { }
            """));
    }

    [Fact]
    public void MethodParameterCapturedByAHandler_IsReported()
    {
        // The element was passed in already built; only the lambda's *own* parameters
        // are fresh.
        Assert.Single(AnalyzeInClass("""
            public void Wire(BoxElement pane) => Register(() => { pane.Flex.FlexBasis = Dimension.Px(200); });
            """));
    }

    [Fact]
    public void ElementReachedThroughALocal_IsReported()
    {
        // `pair` is local to the lambda, but `pair.Panel` is not — member accesses are
        // not followed for exactly this reason.
        Assert.Single(AnalyzeInClass("""
            private sealed class Pair { public BoxElement Panel = Box.New(); }
            public void Wire() => Register(() =>
            {
                var pair = new Pair();
                pair.Panel.Box.Width = Dimension.Px(10);
            });
            """));
    }

    [Fact]
    public void FollowedByMarkLayoutDirty_IsNotReported()
    {
        Assert.Empty(Analyze("""
            var el = Box.New();
            Register(() =>
            {
                el.Flex.FlexBasis = Dimension.Px(200);
                el.MarkLayoutDirty();
            });
            """));
    }

    [Fact]
    public void MarkLayoutDirtyAsAMethodGroup_IsNotReported()
    {
        // Animate.Width and friends hand the invalidation over as a delegate rather
        // than calling it, so matching only invocations would report the framework's
        // own correct code.
        Assert.Empty(Analyze("""
            var el = Box.New();
            Register(() => Tween(v => el.Box.Width = v, el.MarkLayoutDirty));
            """));
    }

    [Fact]
    public void StructuralMutation_CountsAsInvalidation()
    {
        // VirtualListElement resizes its spacer and then replaces children, which
        // invalidates on the way through.
        Assert.Empty(Analyze("""
            var el = Box.New();
            var spacer = Box.New();
            Register(() =>
            {
                spacer.Box.Height = Dimension.Px(40);
                el.ReplaceChildren(new Element[] { spacer });
            });
            """));
    }

    [Fact]
    public void FluentBuilderExtension_IsNotReported()
    {
        // The framework's own ElementExtensions shape, and any app that writes its own.
        Assert.Empty(AnalyzeInClass("""
            public static T Grow<T>(T el, float g) where T : Element { el.Flex.FlexGrow = g; return el; }
            """));
    }

    [Fact]
    public void SelfInvalidatingVisualProperty_IsNotReported()
    {
        // BackgroundColor's setter already calls MarkPaintDirty, so it is not the trap.
        Assert.Empty(Analyze("""
            var el = Box.New();
            Register(() => { el.BackgroundColor = Color.Red; });
            """));
    }

    // ──── Harness ────

    private static ImmutableArray<Diagnostic> Analyze(string body) => AnalyzeInClass($$"""
        public void Run()
        {
        {{body}}
        }
        """);

    private static ImmutableArray<Diagnostic> AnalyzeInClass(string members)
    {
        var source = $$"""
            using System;
            using HultUI.Core.Builders;
            using HultUI.Core.Elements;
            using HultUI.Core.Events;
            using HultUI.Core.Layout;
            using HultUI.Core.Primitives;

            public class Harness
            {
                {{members}}

                private static void Register(Action handler) { }
                private static void Register(Action<PointerEvent> handler) { }
                private static void Tween(Action<Dimension> apply, Action onChanged) { }
            }
            """;

        var compilation = CSharpCompilation.Create(
            "AnalyzerTest",
            [CSharpSyntaxTree.ParseText(source)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var withAnalyzer = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new StyleMutationAnalyzer()));

        var diagnostics = withAnalyzer.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();

        // A snippet that does not compile would silently analyze to nothing, so the
        // absence assertions would all pass for the wrong reason.
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.True(errors.Length == 0,
            "test snippet does not compile: " + string.Join("; ", errors.Select(e => e.ToString())));

        return diagnostics;
    }

    private static readonly PortableExecutableReference[] References =
    [
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Element).Assembly.Location),
        .. Directory.GetFiles(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll")
            .Select(p => MetadataReference.CreateFromFile(p)),
    ];
}
