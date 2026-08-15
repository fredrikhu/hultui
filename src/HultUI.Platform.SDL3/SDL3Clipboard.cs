using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HultUI.Core.Platform;
using static SDL.SDL3;

namespace HultUI.Platform.SDL3;

/// <summary>
/// The system clipboard through SDL3.
///
/// <para>SDL models a clipboard owner rather than a value: you declare which MIME types
/// you can produce, and SDL calls back for the bytes if some app actually asks for one.
/// That is why <see cref="SetData"/> takes every representation at once — the offer is
/// made as a set, and making a second offer replaces the first rather than adding to it.
/// It also means the bytes have to outlive the call, so each offer is held in a table
/// until SDL says the clipboard changed hands.</para>
///
/// <para><b>Windows, multiple formats.</b> SDL's Windows backend serves the first offered
/// format's bytes whatever type is asked for, so a two-format offer reads back as one
/// format twice. Reproduced with a single-format offer working correctly and a two-format
/// offer failing in both orders, which puts it below this class rather than in it. The
/// fix is direct Win32 clipboard calls or an SDL change; until then, offer one format on
/// Windows. <see cref="GetText"/> asks for the text alternative by name so it is at least
/// right whenever the platform answers honestly.</para>
/// </summary>
public sealed class SDL3Clipboard : IClipboard
{
    private readonly SDL3Window _window;

    public SDL3Clipboard(SDL3Window window)
    {
        _window = window;
    }

    public string? GetText()
    {
        // Ask for the text alternative by name first. When the clipboard is owned as typed
        // data — by us or by another app offering several formats — SDL_GetClipboardText
        // does not reliably pick the text one out of the set, and copying "diff as HTML
        // plus plain text" would then paste markup into a plain-text field.
        if (HasFormat(ClipboardFormats.PlainText) && GetData(ClipboardFormats.PlainText) is { } bytes)
            return System.Text.Encoding.UTF8.GetString(bytes);

        return _window.GetClipboardText();
    }

    public void SetText(string text) => _window.SetClipboardText(text);

    // ──── Offered data ────
    //
    // One entry per SetData call, keyed by the integer handed to SDL as userdata. The
    // native callbacks cannot capture, so this is how they find the bytes again. Entries
    // are dropped by SDL's cleanup callback, which fires when another offer (ours or
    // another app's) takes the clipboard.

    private static readonly ConcurrentDictionary<nint, Offer> Offers = new();
    private static int _nextId;

    private sealed class Offer
    {
        public required Dictionary<string, byte[]> Data { get; init; }
        public required List<GCHandle> Pins { get; init; }
    }

    public unsafe bool HasFormat(string mimeType)
    {
        var bytes = SDL3Window.MarshalUtf8(mimeType);
        fixed (byte* ptr = bytes)
            return SDL_HasClipboardData(ptr);
    }

    public unsafe byte[]? GetData(string mimeType)
    {
        var mime = SDL3Window.MarshalUtf8(mimeType);
        fixed (byte* mimePtr = mime)
        {
            nuint size = 0;
            nint data = SDL_GetClipboardData(mimePtr, &size);
            if (data == 0) return null;

            try
            {
                if (size == 0) return [];
                var managed = new byte[size];
                Marshal.Copy(data, managed, 0, (int)size);
                return managed;
            }
            finally
            {
                // SDL_GetClipboardData hands over an allocation we own.
                SDL_free(data);
            }
        }
    }

    public unsafe void SetData(IReadOnlyDictionary<string, byte[]> data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Count == 0)
        {
            Clear();
            return;
        }

        nint id = Interlocked.Increment(ref _nextId);
        var copy = new Dictionary<string, byte[]>(data.Count, StringComparer.Ordinal);
        var pins = new List<GCHandle>(data.Count);
        foreach (var (mime, bytes) in data)
        {
            // Copy so a caller reusing its buffer cannot change what a later paste yields.
            var owned = bytes.ToArray();
            copy[mime] = owned;
            if (owned.Length > 0)
                pins.Add(GCHandle.Alloc(owned, GCHandleType.Pinned));
        }

        Offers[id] = new Offer { Data = copy, Pins = pins };

        var mimeTypes = new string[copy.Count];
        copy.Keys.CopyTo(mimeTypes, 0);

        delegate* unmanaged[Cdecl]<nint, byte*, nuint*, nint> provide = &Provide;
        delegate* unmanaged[Cdecl]<nint, void> release = &Release;
        if (!SDL_SetClipboardData(provide, release, id, mimeTypes))
        {
            // SDL rejected the offer, so its cleanup callback will never run.
            ReleaseOffer(id);
        }
    }

    public void Clear()
    {
        SDL_ClearClipboardData();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe nint Provide(nint userdata, byte* mimeType, nuint* size)
    {
        *size = 0;
        if (!Offers.TryGetValue(userdata, out var offer)) return 0;

        var mime = Marshal.PtrToStringUTF8((nint)mimeType);
        if (mime is null || !offer.Data.TryGetValue(mime, out var bytes) || bytes.Length == 0)
            return 0;

        // Pinned for the lifetime of the offer, so handing out the address is safe until
        // Release runs. The pins are parallel to the non-empty entries, so find by identity
        // rather than by index.
        foreach (var pin in offer.Pins)
        {
            if (!ReferenceEquals(pin.Target, bytes)) continue;
            *size = (nuint)bytes.Length;
            return pin.AddrOfPinnedObject();
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Release(nint userdata) => ReleaseOffer(userdata);

    /// <summary>The managed half of <see cref="Release"/>, so the failure path can drop an
    /// offer SDL never accepted — an UnmanagedCallersOnly method cannot be called directly.</summary>
    private static void ReleaseOffer(nint userdata)
    {
        if (!Offers.TryRemove(userdata, out var offer)) return;
        foreach (var pin in offer.Pins)
            pin.Free();
    }
}
