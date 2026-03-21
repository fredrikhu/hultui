using System.Reflection;
using HultUI.Core.Reactivity;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace HultUI.SourceGenerators.Tests;

public class ReactiveGeneratorTests
{
    [Fact]
    public void BasicPartialProperty_GeneratesSignalBacking()
    {
        var source = """
            using HultUI.Core.Reactivity;
            using HultUI.Core.Components;
            using HultUI.Core.Elements;
            using HultUI.Core.Builders;

            public partial class MyComp : Component
            {
                [Reactive] public partial int Count { get; set; }
                public override Element Build() => Box.New();
            }
            """;

        var (output, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("Signal<int>", output);
        Assert.Contains("__count_signal", output);
        Assert.Contains("public partial int Count", output);
        Assert.Contains("get => __count_signal.Value;", output);
        Assert.Contains("set => __count_signal.Value = value;", output);
    }

    [Fact]
    public void StringProperty_GeneratesCorrectType()
    {
        var source = """
            using HultUI.Core.Reactivity;
            using HultUI.Core.Components;
            using HultUI.Core.Elements;
            using HultUI.Core.Builders;

            public partial class MyComp : Component
            {
                [Reactive] public partial string Name { get; set; }
                public override Element Build() => Box.New();
            }
            """;

        var (output, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("Signal<string>", output);
        Assert.Contains("__name_signal", output);
    }

    [Fact]
    public void MultipleProperties_GeneratesAll()
    {
        var source = """
            using HultUI.Core.Reactivity;
            using HultUI.Core.Components;
            using HultUI.Core.Elements;
            using HultUI.Core.Builders;

            public partial class MyComp : Component
            {
                [Reactive] public partial int X { get; set; }
                [Reactive] public partial int Y { get; set; }
                [Reactive] public partial string Label { get; set; }
                public override Element Build() => Box.New();
            }
            """;

        var (output, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("__x_signal", output);
        Assert.Contains("__y_signal", output);
        Assert.Contains("__label_signal", output);
    }

    [Fact]
    public void NamespacedClass_GeneratesWithNamespace()
    {
        var source = """
            using HultUI.Core.Reactivity;
            using HultUI.Core.Components;
            using HultUI.Core.Elements;
            using HultUI.Core.Builders;

            namespace MyApp.Components
            {
                public partial class Counter : Component
                {
                    [Reactive] public partial int Count { get; set; }
                    public override Element Build() => Box.New();
                }
            }
            """;

        var (output, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("namespace MyApp.Components;", output);
        Assert.Contains("public partial class Counter", output);
    }

    [Fact]
    public void NonPartialClass_ReportsDiagnostic()
    {
        var source = """
            using HultUI.Core.Reactivity;
            using HultUI.Core.Components;
            using HultUI.Core.Elements;
            using HultUI.Core.Builders;

            public class MyComp : Component
            {
                [Reactive] public int Count { get; set; }
                public override Element Build() => Box.New();
            }
            """;

        var (_, diagnostics) = RunGenerator(source);

        var hultDiag = diagnostics.Where(d => d.Id == "HULTUI001").ToList();
        Assert.Single(hultDiag);
        Assert.Contains("MyComp", hultDiag[0].GetMessage());
        Assert.Contains("Count", hultDiag[0].GetMessage());
    }

    [Fact]
    public void NullableProperty_UsesDefaultInitializer()
    {
        var source = """
            using HultUI.Core.Reactivity;
            using HultUI.Core.Components;
            using HultUI.Core.Elements;
            using HultUI.Core.Builders;

            public partial class MyComp : Component
            {
                [Reactive] public partial string? Name { get; set; }
                public override Element Build() => Box.New();
            }
            """;

        var (output, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("new(default)", output);
    }

    private static (string generatedSource, IReadOnlyList<Diagnostic> diagnostics) RunGenerator(string source)
    {
        var coreAssembly = typeof(ReactiveAttribute).Assembly;
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(coreAssembly.Location),
        };

        // Add runtime references
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var name in new[] { "System.Runtime", "System.Collections", "netstandard" })
        {
            var path = Path.Combine(runtimeDir, name + ".dll");
            if (File.Exists(path))
                references.Add(MetadataReference.CreateFromFile(path));
        }

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ReactiveGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var genDiagnostics);

        var results = driver.GetRunResult();
        var generatedSource = string.Join("\n", results.GeneratedTrees.Select(t => t.GetText().ToString()));

        // Combine generator diagnostics with compilation diagnostics
        var allDiagnostics = genDiagnostics.AddRange(
            outputCompilation.GetDiagnostics().Where(d => d.Id.StartsWith("HULTUI")));

        return (generatedSource, allDiagnostics);
    }
}
