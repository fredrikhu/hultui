using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace HultUI.Platform.Accessibility.Windows;

/// <summary>
/// Minimal Win32 VARIANT bindings. UIA's <c>GetPropertyValue</c> returns values
/// through a VARIANT out-parameter; we fill it by value for primitive types
/// and via <c>SysAllocString</c> for strings. Callers must <see cref="Free"/>
/// strings that were set this way (UIA does it for us once it copies).
/// </summary>
[SupportedOSPlatform("windows")]
[StructLayout(LayoutKind.Explicit, Size = 16)]
internal struct UiaVariant
{
    // A Win32 VARIANT is exactly 16 bytes on both x86 and x64: VARTYPE
    // (ushort) at offset 0, 6 bytes of reserved header, then an 8-byte value
    // union at offset 8 (DECIMAL aliases the whole 16 bytes). GetPropertyValue
    // writes this struct into a VARIANT that UIA allocated, so the size MUST
    // match — an oversized struct overruns UIA's buffer (0xC0000409).
    [FieldOffset(0)] public ushort Vt;
    [FieldOffset(8)] public IntPtr PointerValue;
    [FieldOffset(8)] public int IntValue;
    [FieldOffset(8)] public double DoubleValue;
    [FieldOffset(8)] public short ShortValue;

    // ──── VARTYPE values we use
    public const ushort VT_EMPTY = 0;
    public const ushort VT_I4 = 3;
    public const ushort VT_R8 = 5;
    public const ushort VT_BSTR = 8;
    public const ushort VT_BOOL = 11;
    public const ushort VT_UNKNOWN = 13;

    public static UiaVariant Empty => default;
    public static UiaVariant FromInt(int v) => new() { Vt = VT_I4, IntValue = v };
    public static UiaVariant FromDouble(double v) => new() { Vt = VT_R8, DoubleValue = v };

    /// <summary>VARIANT_BOOL: -1 for true, 0 for false.</summary>
    public static UiaVariant FromBool(bool v) =>
        new() { Vt = VT_BOOL, ShortValue = v ? (short)-1 : (short)0 };

    /// <summary>A COM object; the VARIANT holds the reference the caller passes in.</summary>
    public static UiaVariant FromUnknown(IntPtr unknown) => new() { Vt = VT_UNKNOWN, PointerValue = unknown };

    public static UiaVariant FromBStr(string s) =>
        new() { Vt = VT_BSTR, PointerValue = Marshal.StringToBSTR(s) };

    /// <summary>The BSTR value as a string, or null when this VARIANT isn't a string.</summary>
    public readonly string? AsString() =>
        Vt == VT_BSTR && PointerValue != IntPtr.Zero ? Marshal.PtrToStringBSTR(PointerValue) : null;

    /// <summary>
    /// Release any heap allocation owned by this VARIANT (currently only BSTR).
    /// Called on local stack VARIANTs we chose not to hand off to UIA — once
    /// UIA owns the VARIANT, it's responsible for freeing.
    /// </summary>
    public void Free()
    {
        if (Vt == VT_BSTR && PointerValue != IntPtr.Zero)
            Marshal.FreeBSTR(PointerValue);
        this = default;
    }
}
