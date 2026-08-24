using System.Collections.Immutable;
using HultUI.Core.Elements;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace HultUI.SourceGenerators.Tests;

/// <summary>
/// HULTUI003 catches an identity comparison that stops matching once the tree has been
/// reconciled once. As with HULTUI002, the tests that matter most are the ones asserting
/// silence: comparing two members of the same event is a correct and common idiom, and a rule
/// that flagged it would be worse than no rule.
/// </summary>
public class ElementIdentityAnalyzerTests
{
    // ──── Shapes that must be reported ────

    [Fact]
    public void ReferenceEquals_AgainstACapturedElement_IsReported()
    {
        // The mistake this is calibrated on: a checkbox built in the same pass, compared
        // against to decide whether the row's own handler should also run. True on the first
        // frame, false from the second, so the row silently double-handled every click.
        var diagnostics = Analyze("""
            var box = Box.New();
            Register(e => { if (object.ReferenceEquals(e.Target, box)) return; });
            """);

        var d = Assert.Single(diagnostics);
        Assert.Equal("HULTUI003", d.Id);
        Assert.Contains("Target", d.GetMessage());
        Assert.Contains("AutomationId", d.GetMessage());
    }

    [Fact]
    public void EqualityOperator_AgainstACapturedElement_IsReported()
    {
        Assert.Single(Analyze("""
            var box = Box.New();
            Register(e => { if (e.Target == box) return; });
            """));
    }

    [Fact]
    public void Inequality_IsReportedToo() =>
        Assert.Single(Analyze("""
            var box = Box.New();
            Register(e => { if (e.Target != box) return; });
            """));

    [Fact]
    public void CurrentTarget_AgainstACapturedElement_IsReported()
    {
        var d = Assert.Single(Analyze("""
            var box = Box.New();
            Register(e => { if (e.CurrentTarget == box) return; });
            """));
        Assert.Contains("CurrentTarget", d.GetMessage());
    }

    [Fact]
    public void ComparingAgainstAField_IsReported() =>
        Assert.Single(AnalyzeInClass("""
            private readonly BoxElement _row = Box.New();
            public void Wire() => Register(e => { if (e.Target == _row) return; });
            """));

    [Fact]
    public void TheElementOnEitherSide_IsReported() =>
        Assert.Single(Analyze("""
            var box = Box.New();
            Register(e => { if (box == e.Target) return; });
            """));

    // ──── Shapes that must stay quiet ────

    [Fact]
    public void TargetAgainstCurrentTarget_IsNotReported() =>
        // "Was the hit on me, or on something below me" — correct, and the reason the rule
        // needs an exclusion at all. Vesper's file rows use exactly this.
        Assert.Empty(Analyze("""
            Register(e => { if (object.ReferenceEquals(e.Target, e.CurrentTarget)) return; });
            """));

    [Fact]
    public void CurrentTargetAgainstTarget_IsNotReported() =>
        Assert.Empty(Analyze("""
            Register(e => { if (e.CurrentTarget == e.Target) return; });
            """));

    [Fact]
    public void ANullCheck_IsNotReported() =>
        // Asking whether there was a target at all says nothing about identity.
        Assert.Empty(Analyze("""
            Register(e => { if (e.Target == null) return; });
            """));

    [Fact]
    public void ARefsCurrent_IsNotReported() =>
        // A Ref is repointed at the live element on every patch, so this comparison is
        // correct — and it is the fix the diagnostic recommends.
        Assert.Empty(AnalyzeInClass("""
            private readonly Ref<BoxElement> _rowRef = new();
            public void Wire() => Register(e => { if (e.Target == _rowRef.Current) return; });
            """));

    [Fact]
    public void ATypeTest_IsNotReported() =>
        // The recommended alternative, which survives a patch.
        Assert.Empty(Analyze("""
            Register(e => { if (e.Target is BoxElement) return; });
            """));

    [Fact]
    public void ComparingAutomationIds_IsNotReported() =>
        Assert.Empty(Analyze("""
            Register(e => { if (e.Target.AutomationId == "save") return; });
            """));

    [Fact]
    public void AnUnrelatedTargetProperty_IsNotReported() =>
        // Plenty of things have a Target that is not an element — a weak reference, a GC
        // handle. The rule keys on the declaring type being an ElementEvent.
        Assert.Empty(AnalyzeInClass("""
            public void Wire()
            {
                var weak = new System.WeakReference(new object());
                var other = new object();
                if (object.ReferenceEquals(weak.Target, other)) return;
            }
            """));

    [Fact]
    public void ComparingTwoOrdinaryElements_IsNotReported() =>
        // No event involved, so no lifetime claim to make.
        Assert.Empty(Analyze("""
            var a = Box.New();
            var b = Box.New();
            if (a == b) return;
            """));

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
            using HultUI.Core.Reactivity;

            public class Harness
            {
                {{members}}

                private static void Register(Action handler) { }
                private static void Register(Action<PointerEvent> handler) { }
            }
            """;

        var compilation = CSharpCompilation.Create(
            "AnalyzerTest",
            [CSharpSyntaxTree.ParseText(source)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var withAnalyzer = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new ElementIdentityAnalyzer()));

        var diagnostics = withAnalyzer.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();

        // A snippet that does not compile would analyze to nothing, and every absence
        // assertion above would pass for the wrong reason.
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
