using System.Reflection;
using System.Runtime.CompilerServices;
using Porta.Pty;
using XTerm;
using XTerm.Buffer;
using static XtermNet.Probe;

namespace XtermNet;

/// <summary>
/// Question 1: what consuming the two packages brings. Versions, what lands beside the program,
/// what loads, how big a cell is, and which ConPTY Porta.Pty picked. The AOT half of the question
/// is a publish, run from the shell (README).
/// </summary>
internal static class Q1
{
    public static int Run()
    {
        using var t = new Terminal();
        t.Write("x");
        _ = PtyProvider.PseudoConsoleImplementation;

        foreach (var a in AppDomain.CurrentDomain.GetAssemblies()
                     .Where(a => !a.IsDynamic && !(a.GetName().Name ?? "").StartsWith("System") && a.GetName().Name is not ("mscorlib" or "netstandard"))
                     .OrderBy(a => a.GetName().Name))
        {
            var version = a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? a.GetName().Version?.ToString();
            Log($"loaded  {a.GetName().Name,-28} {version}");
        }

        Log("");
        Log($"sizeof(BufferCell)    {Unsafe.SizeOf<BufferCell>()} B");
        Log($"sizeof(AttributeData) {Unsafe.SizeOf<AttributeData>()} B");
        Log($"BufferCell has references: {RuntimeHelpers.IsReferenceOrContainsReferences<BufferCell>()}");
        Log($"ConPTY implementation: {PtyProvider.PseudoConsoleImplementation}");

        Log("");
        Log($"output directory {AppContext.BaseDirectory}");
        long total = 0;
        foreach (var f in Directory.EnumerateFiles(AppContext.BaseDirectory, "*", SearchOption.AllDirectories)
                     .Where(f => !f.EndsWith(".pdb") && !f.EndsWith(".deps.json") && !f.EndsWith(".runtimeconfig.json"))
                     .OrderBy(f => f))
        {
            var len = new FileInfo(f).Length;
            total += len;
            Log($"  {Path.GetRelativePath(AppContext.BaseDirectory, f),-60} {len / 1024.0,9:0.0} KB");
        }
        Log($"  total {total / 1024.0 / 1024.0:0.00} MB");
        return 0;
    }
}
