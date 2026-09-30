using System.Runtime.InteropServices;

namespace HultUI.WebView.WebView2.Interop;

/// <summary>
/// A data object holding files and text, for a drag the platform delivered without one — a
/// HultUI window whose drop target is not HultUI's own (an MTA thread, a WinForms host). The
/// shell makes the object; we only fill it (the approach of the platform's drag-out code).
/// </summary>
internal static unsafe partial class DropData
{
    private const uint CF_UNICODETEXT = 13, CF_HDROP = 15;
    private const uint TYMED_HGLOBAL = 1, DVASPECT_CONTENT = 1, GMEM_MOVEABLE = 0x0002;
    private static readonly Guid IID_IDataObject = new("0000010e-0000-0000-c000-000000000046");

    /// <summary>An <c>IDataObject*</c> the caller owns, or zero.</summary>
    public static nint Create(IReadOnlyList<string> files, string? text)
    {
        if (files.Count == 0 && text is null) return 0;
        nint data;
        Guid iid = IID_IDataObject;
        if (SHCreateDataObject(0, 0, null, 0, &iid, &data) < 0 || data == 0) return 0;

        if (files.Count > 0)
        {
            // CF_HDROP: a DROPFILES header, then each path NUL-terminated, then one more NUL.
            int chars = files.Sum(f => f.Length + 1) + 1;
            var memory = GlobalAlloc(GMEM_MOVEABLE, (nuint)(sizeof(DROPFILES) + chars * sizeof(char)));
            if (memory != 0)
            {
                var header = (DROPFILES*)GlobalLock(memory);
                header->Files = (uint)sizeof(DROPFILES);
                header->Wide = 1;
                var names = (char*)((byte*)header + sizeof(DROPFILES));
                foreach (var file in files)
                {
                    file.AsSpan().CopyTo(new Span<char>(names, file.Length));
                    names += file.Length;
                    *names++ = '\0';
                }
                *names = '\0';
                GlobalUnlock(memory);
                SetData(data, CF_HDROP, memory);
            }
        }
        if (text is not null)
        {
            var memory = GlobalAlloc(GMEM_MOVEABLE, (nuint)((text.Length + 1) * sizeof(char)));
            if (memory != 0)
            {
                var chars = (char*)GlobalLock(memory);
                text.AsSpan().CopyTo(new Span<char>(chars, text.Length));
                chars[text.Length] = '\0';
                GlobalUnlock(memory);
                SetData(data, CF_UNICODETEXT, memory);
            }
        }
        return data;
    }

    /// <summary><c>IDataObject::SetData</c>, slot 7, handing the memory over; freed here if refused.</summary>
    private static void SetData(nint data, uint format, nint memory)
    {
        var request = new FORMATETC { Format = (ushort)format, Aspect = DVASPECT_CONTENT, Index = -1, Tymed = TYMED_HGLOBAL };
        var medium = new STGMEDIUM { Tymed = TYMED_HGLOBAL, Handle = memory };
        int hr = ((delegate* unmanaged[Stdcall]<nint, FORMATETC*, STGMEDIUM*, int, int>)(*(nint**)data)[7])(data, &request, &medium, 1);
        if (hr < 0) GlobalFree(memory);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DROPFILES
    {
        public uint Files;
        public int X, Y;
        public int NonClient;
        public int Wide;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FORMATETC
    {
        public ushort Format;
        public nint TargetDevice;
        public uint Aspect;
        public int Index;
        public uint Tymed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STGMEDIUM
    {
        public uint Tymed;
        public nint Handle;
        public nint UnkForRelease;
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHCreateDataObject(nint folder, uint count, nint* items, nint inner, Guid* iid, nint* result);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalFree(nint memory);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalLock(nint memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint memory);
}
