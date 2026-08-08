// The element factories capture their call site so the DevTools inspector can say
// where an element came from. Only the *attributes* are conditional, never the
// parameters: the signature is identical either way, so assemblies built with
// origins on and off stay binary compatible, and a build without the define emits
// the parameter defaults at each call site rather than a string literal — no source
// paths in the binary, nothing to pass but two immediates.
#if HULTUI_ELEMENT_ORIGIN
global using OriginFile = System.Runtime.CompilerServices.CallerFilePathAttribute;
global using OriginLine = System.Runtime.CompilerServices.CallerLineNumberAttribute;
#else
global using OriginFile = HultUI.Core.Elements.InertCallerInfoAttribute;
global using OriginLine = HultUI.Core.Elements.InertCallerInfoAttribute;
#endif

namespace HultUI.Core.Elements;

/// <summary>
/// Where an element was created: the source file and line of the factory call that
/// produced it. Available only in builds with <c>HultUIElementOrigin</c> enabled
/// (see Directory.Build.targets); elsewhere <see cref="Element.Origin"/> is null.
/// </summary>
public readonly record struct ElementOrigin(string File, int Line)
{
    /// <summary>The file name without its directory — what a panel row shows.</summary>
    public string FileName
    {
        get
        {
            int slash = File.AsSpan().LastIndexOfAny('/', '\\');
            return slash < 0 ? File : File[(slash + 1)..];
        }
    }

    /// <summary>e.g. <c>CommanderApp.Pane.cs:214</c>.</summary>
    public override string ToString() => $"{FileName}:{Line}";
}

/// <summary>
/// Stands in for the caller-info attributes when origins are compiled out. It carries
/// no behaviour: with no <c>[CallerFilePath]</c> present the compiler simply passes
/// the parameter's default, which is the whole point.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
internal sealed class InertCallerInfoAttribute : Attribute;

internal static class ElementOriginExtensions
{
    /// <summary>Stamps a freshly built element with the call site that produced it.</summary>
    internal static T WithOrigin<T>(this T element, string? file, int line) where T : Element
    {
        element.SetOrigin(file, line);
        return element;
    }
}
