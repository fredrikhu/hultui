using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace HultUI.SourceGenerators;

/// <summary>
/// Flags a layout-style mutation that will be silently dropped.
///
/// <para><c>HultUI.Core.Layout.FlexStyle</c> and friends are plain classes with
/// auto-property setters, so <c>el.Flex.FlexBasis = x</c> does not mark anything dirty.
/// During construction that is harmless — a new element starts fully dirty — but in a
/// handler that runs after the first layout pass the next frame sees no reason to re-run
/// layout and the change never reaches the screen. Nothing throws, nothing logs; the UI
/// just doesn't move. This cost real debugging time in a split-pane drag handler, which
/// is what the rule is calibrated on.</para>
///
/// <para>Auto-invalidating setters were considered and rejected: each style object would
/// need a back-pointer to its element and every write would walk to the root, so static
/// UI construction would pay for a problem it doesn't have. Explicit invalidation stays
/// the contract, and this rule is how the contract gets enforced.</para>
///
/// <para><b>Calibration.</b> The three apps in this repo contain ~170 direct style
/// assignments, and nearly all are construction-time and correct. A rule that flagged
/// them would be noise, and warning noise trains people to stop reading the build output
/// — the exact failure this is meant to prevent. So the rule only fires inside a lambda
/// or anonymous method (where post-layout imperative code lives) on an element that
/// predates that lambda. Post-layout mutation from a named method is a real miss; widen
/// the rule when a real case turns up rather than on suspicion.</para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StyleMutationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "HULTUI002";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Layout style mutated without invalidating layout",
        messageFormat: "'{0}' is assigned on an element that is already laid out, but nothing " +
                       "invalidates it — call {1} after the assignment or the change is dropped",
        category: "HultUI.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Style objects have plain auto-property setters and do not invalidate. " +
                     "Inside a handler the element has already been laid out, so the next frame " +
                     "sees no reason to re-run layout and the change is silently lost.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <summary>The style objects hanging off Element that do not self-invalidate.</summary>
    private static readonly string[] StyleTypes =
    [
        "HultUI.Core.Layout.FlexStyle",
        "HultUI.Core.Layout.BoxStyle",
        "HultUI.Core.Layout.PositionStyle",
        "HultUI.Core.Layout.GridStyle",
        "HultUI.Core.Layout.GridItemStyle",
        "HultUI.Core.Styling.InheritedStyle",
    ];

    /// <summary>
    /// Inherited properties that change text metrics. Re-resolving the style is not enough
    /// for these: the style pass recomputes the value, but the text keeps whatever size it
    /// was last measured at until layout runs again. The rest (TextColor, Decoration,
    /// Cursor) only need the style pass.
    /// </summary>
    private static readonly HashSet<string> MetricAffecting =
    [
        "FontFamily", "FontSize", "FontWeight", "FontSlant",
        "LineHeight", "LetterSpacing", "TextAlign", "TextDirection",
    ];

    /// <summary>
    /// Names that make a body safe. The Mark* trio is the direct fix; the structural
    /// mutators invalidate on the way through, which is how VirtualListElement gets away
    /// with resizing its spacer. Matched by name only — a method group counts as much as
    /// a call, because <c>Animate</c> passes <c>el.MarkLayoutDirty</c> as a delegate.
    /// </summary>
    private static readonly HashSet<string> InvalidatingNames =
    [
        "MarkLayoutDirty", "MarkPaintDirty", "MarkStyleDirty", "MarkDirty",
        "ReplaceChildren", "AddChild", "InsertChild", "RemoveChild", "ClearChildren",
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var element = start.Compilation.GetTypeByMetadataName("HultUI.Core.Elements.Element");
            if (element is null) return;   // not a HultUI compilation; nothing to say

            var styles = StyleTypes
                .Select(start.Compilation.GetTypeByMetadataName)
                .Where(t => t is not null)
                .ToImmutableArray();
            if (styles.Length == 0) return;

            start.RegisterOperationAction(ctx => Analyze(ctx, element, styles), OperationKind.SimpleAssignment);
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol elementType,
        ImmutableArray<INamedTypeSymbol?> styleTypes)
    {
        var assignment = (IAssignmentOperation)context.Operation;

        // The target must be a property on one of the style objects…
        if (assignment.Target is not IPropertyReferenceOperation target) return;
        var owner = target.Property.ContainingType;
        if (!styleTypes.Any(s => SymbolEqualityComparer.Default.Equals(s, owner))) return;

        // …reached through an element, rather than a style object held on its own.
        if (target.Instance is not IPropertyReferenceOperation styleAccess) return;
        var receiver = styleAccess.Instance;
        if (receiver is null || !DerivesFromElement(receiver.Type, elementType)) return;

        // Only imperative post-layout code is in scope. Construction code assigns styles
        // freely and correctly, because a fresh element is already fully dirty.
        var lambda = EnclosingLambda(assignment.Syntax);
        if (lambda is null) return;

        // An element the lambda itself introduced has not been laid out yet either, so it
        // is construction code that merely happens to live in a callback.
        if (IsIntroducedBy(receiver, lambda)) return;

        if (ScopeInvalidates(assignment.Syntax)) return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule, assignment.Syntax.GetLocation(),
            $"{owner.Name}.{target.Property.Name}", FixFor(owner, target.Property.Name)));
    }

    /// <summary>
    /// The call that actually makes the assignment take effect. Inherited style is the
    /// awkward one: <c>MarkStyleDirty</c> re-resolves the value but does not re-measure,
    /// so a font change needs both passes or the text redraws at its old size.
    /// </summary>
    private static string FixFor(INamedTypeSymbol styleType, string property)
    {
        if (styleType.Name == "InheritedStyle")
            return MetricAffecting.Contains(property)
                ? "MarkStyleDirty() and MarkLayoutDirty()"
                : "MarkStyleDirty()";

        // ZIndex changes paint order, not geometry, so it needs the cheaper of the two.
        return property == "ZIndex" ? "MarkPaintDirty()" : "MarkLayoutDirty()";
    }

    private static bool DerivesFromElement(ITypeSymbol? type, INamedTypeSymbol elementType)
    {
        for (var t = type; t is not null; t = t.BaseType)
            if (SymbolEqualityComparer.Default.Equals(t, elementType))
                return true;
        return false;
    }

    /// <summary>
    /// The innermost lambda or anonymous method containing <paramref name="node"/>, or null
    /// when a member declaration is reached first (ordinary method body).
    /// </summary>
    private static SyntaxNode? EnclosingLambda(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is AnonymousFunctionExpressionSyntax) return current;
            if (current is MemberDeclarationSyntax or AccessorDeclarationSyntax) return null;
        }
        return null;
    }

    /// <summary>
    /// True when the element being mutated is named by a local or parameter that
    /// <paramref name="lambda"/> introduced — a popup built inside a click handler, or the
    /// subject of a configuration callback such as <c>AnimatedBox(…, box =&gt; …)</c>,
    /// where the element arrives fresh from the factory.
    ///
    /// <para>Member accesses are deliberately not followed. <c>e.Target</c> roots in the
    /// lambda's own parameter but names an element built and laid out long before, and the
    /// same goes for any element reached through a field or property of something local.
    /// Only the element the callback was handed <i>directly</i> counts as fresh.</para>
    ///
    /// <para>A parameter of the enclosing <i>method</i> does not qualify: a captured
    /// element passed into a wiring method and mutated inside a handler is precisely the
    /// split-pane bug.</para>
    /// </summary>
    private static bool IsIntroducedBy(IOperation receiver, SyntaxNode lambda)
    {
        var root = receiver;
        while (root is IConversionOperation conversion)
            root = conversion.Operand;

        ISymbol? symbol = root switch
        {
            ILocalReferenceOperation local => local.Local,
            IParameterReferenceOperation parameter => parameter.Parameter,
            _ => null,
        };
        if (symbol is null) return false;

        foreach (var reference in symbol.DeclaringSyntaxReferences)
            if (reference.SyntaxTree == lambda.SyntaxTree && lambda.Span.Contains(reference.Span))
                return true;
        return false;
    }

    /// <summary>
    /// True when anything in the whole enclosing member invalidates — not just the lambda
    /// holding the assignment. <c>Animate.Width</c> is the reason for the wider scope: it
    /// mutates inside <c>v =&gt; el.Box.Width = v</c> and hands the invalidation to the
    /// tween builder outside that lambda, so scanning the lambda alone reports the
    /// framework's own correct code.
    ///
    /// <para>Deliberately not order-sensitive and not tied to the specific element. That
    /// does mean a long method which invalidates somewhere suppresses the rule throughout,
    /// which is the intended trade: this rule catches "you forgot entirely", and every
    /// stricter reading tried here reported correct code.</para>
    /// </summary>
    private static bool ScopeInvalidates(SyntaxNode node)
    {
        var scope = node;
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            scope = current;
            if (current is MemberDeclarationSyntax or AccessorDeclarationSyntax) break;
        }

        return scope.DescendantNodes()
            .OfType<SimpleNameSyntax>()
            .Any(name => InvalidatingNames.Contains(name.Identifier.ValueText));
    }
}
