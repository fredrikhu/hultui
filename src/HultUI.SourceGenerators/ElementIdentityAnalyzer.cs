using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace HultUI.SourceGenerators;

/// <summary>
/// Flags an identity comparison between an event's target and an element from somewhere else,
/// which stops matching after the first frame.
///
/// <para>A rebuild constructs a fresh element tree; the reconciler patches its values into the
/// live tree and discards it. So an element captured while building — the usual reason to have
/// one on hand to compare against — is not the element the event reports being hit. The
/// comparison is true on the first frame and false forever after, which reads as a handler
/// that works once and then stops.</para>
///
/// <para>The fix is to stop asking about identity. What the code almost always wants is either
/// "which kind of thing was hit" (<c>e.Target is CheckboxElement</c>) or "which named thing was
/// hit" (<c>e.Target.AutomationId == "save"</c>), and both survive a patch. Where identity is
/// genuinely the question, a <c>Ref&lt;T&gt;</c>'s <c>Current</c> is the live element and does
/// compare correctly.</para>
///
/// <para><b>Calibration.</b> Comparing two members of the same event — <c>ReferenceEquals(
/// e.Target, e.CurrentTarget)</c>, the "was this on me or on a descendant" idiom — is correct
/// and common, so it is excluded. That exclusion is the whole of the rule's noise budget: with
/// it, the three apps, the samples and the framework produce no diagnostics at all. Without a
/// bug to point at, a rule that fired on those would just be teaching people to ignore
/// warnings.</para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ElementIdentityAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "HULTUI003";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Event target compared by identity against another element",
        messageFormat: "'{0}' is compared by identity to an element from elsewhere; after the " +
                       "next rebuild that element is a discarded copy and this is never true — " +
                       "test the type ('is'), the AutomationId, or a Ref<T>'s Current instead",
        category: "HultUI.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reconciliation patches a freshly built tree into the live one and discards " +
                     "the built elements, so an element captured during a build is never the one " +
                     "an event reports as its target. The comparison holds for one frame.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <summary>The two members that carry a live element out of an event.</summary>
    private static readonly string[] EventElementMembers = ["Target", "CurrentTarget"];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var element = start.Compilation.GetTypeByMetadataName("HultUI.Core.Elements.Element");
            var elementEvent = start.Compilation.GetTypeByMetadataName("HultUI.Core.Events.ElementEvent");
            if (element is null || elementEvent is null) return;   // not a HultUI compilation

            start.RegisterOperationAction(
                ctx => AnalyzeBinary(ctx, element, elementEvent), OperationKind.Binary);
            start.RegisterOperationAction(
                ctx => AnalyzeInvocation(ctx, element, elementEvent), OperationKind.Invocation);
        });
    }

    /// <summary><c>e.Target == x</c> and <c>e.Target != x</c>.</summary>
    private static void AnalyzeBinary(OperationAnalysisContext context, INamedTypeSymbol element,
        INamedTypeSymbol elementEvent)
    {
        var binary = (IBinaryOperation)context.Operation;
        if (binary.OperatorKind is not (BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals))
            return;

        Check(context, binary.LeftOperand, binary.RightOperand, element, elementEvent);
    }

    /// <summary><c>ReferenceEquals(e.Target, x)</c> and <c>Equals(e.Target, x)</c>.</summary>
    private static void AnalyzeInvocation(OperationAnalysisContext context, INamedTypeSymbol element,
        INamedTypeSymbol elementEvent)
    {
        var invocation = (IInvocationOperation)context.Operation;
        string name = invocation.TargetMethod.Name;
        if (name is not ("ReferenceEquals" or "Equals")) return;
        if (invocation.Arguments.Length != 2) return;

        Check(context, invocation.Arguments[0].Value, invocation.Arguments[1].Value,
            element, elementEvent);
    }

    /// <summary>
    /// Reports when exactly one side is an event's element member and the other is some other
    /// element. Both sides being event members is the legitimate self-versus-descendant test;
    /// neither being one is not our business.
    /// </summary>
    private static void Check(OperationAnalysisContext context, IOperation left, IOperation right,
        INamedTypeSymbol element, INamedTypeSymbol elementEvent)
    {
        var leftMember = AsEventElementMember(left, elementEvent);
        var rightMember = AsEventElementMember(right, elementEvent);

        // Same event on both sides — "was the hit on me or below me". Correct, and left alone.
        if (leftMember is not null && rightMember is not null) return;

        var (member, other) = leftMember is not null ? (leftMember, right) : (rightMember, left);
        if (member is null) return;

        // The other side has to be an element for the comparison to be the mistake described.
        // A null check is not, and neither is a comparison against something unrelated.
        if (other is IConversionOperation conversion) other = conversion.Operand;
        if (other.ConstantValue is { HasValue: true, Value: null }) return;
        if (!DerivesFrom(other.Type, element)) return;

        // A Ref<T>'s Current is the live element and compares correctly, which is the fix this
        // rule points at — so flagging it would be telling people to undo the fix.
        if (IsRefCurrent(other)) return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule, context.Operation.Syntax.GetLocation(), member));
    }

    /// <summary>
    /// The member name when <paramref name="operation"/> is <c>Target</c> or
    /// <c>CurrentTarget</c> read off something deriving from <c>ElementEvent</c>.
    /// </summary>
    private static string? AsEventElementMember(IOperation operation, INamedTypeSymbol elementEvent)
    {
        if (operation is IConversionOperation conversion) operation = conversion.Operand;
        if (operation is not IPropertyReferenceOperation property) return null;
        if (!EventElementMembers.Contains(property.Property.Name)) return null;
        return DerivesFrom(property.Property.ContainingType, elementEvent)
            ? property.Property.Name
            : null;
    }

    /// <summary>True for <c>someRef.Current</c>, whatever the ref's element type.</summary>
    private static bool IsRefCurrent(IOperation operation) =>
        operation is IPropertyReferenceOperation { Property.Name: "Current" } current
        && current.Property.ContainingType is { Name: "Ref", IsGenericType: true };

    private static bool DerivesFrom(ITypeSymbol? type, INamedTypeSymbol baseType)
    {
        for (var t = type; t is not null; t = t.BaseType)
            if (SymbolEqualityComparer.Default.Equals(t, baseType))
                return true;
        return false;
    }
}
