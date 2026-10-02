using System.Runtime.InteropServices;

namespace HultUI.WebView.Cef.Interop;

/// <summary>
/// Our side of CEF's reference counting, for the structs we implement (handlers, the app,
/// tasks). The rule, from CEF's own wrapper (libcef_dll/cpptoc/cpptoc_ref_counted.h): every
/// struct pointer that crosses the boundary as an argument or a return value carries one
/// reference, owned by whoever receives it; the <c>self</c> argument carries none.
/// </summary>
/// <remarks>
/// Each struct is allocated with a trailer after it: its count and a GC handle to the .NET
/// object it belongs to, which callbacks reach through <see cref="Owner{TOwner}"/>. The
/// creator holds the first reference; CEF's add_ref/release move the count, and the memory and
/// the handle go when it reaches zero.
/// </remarks>
internal static unsafe class CefObject
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Trailer
    {
        public long Count;
        public nint Owner;   // a GCHandle, or zero
    }

    private static int TrailerOffset(int size) => (size + 15) & ~15;

    /// <summary>Allocates a zeroed <typeparamref name="T"/> with one reference, the caller's.</summary>
    public static T* New<T>(object? owner = null) where T : unmanaged
    {
        int offset = TrailerOffset(sizeof(T));
        var p = (byte*)NativeMemory.AllocZeroed((nuint)(offset + sizeof(Trailer)));
        var b = (cef_base_ref_counted_t*)p;
        b->size = (ulong)sizeof(T);
        b->add_ref = &AddRefCb;
        b->release = &ReleaseCb;
        b->has_one_ref = &HasOneRefCb;
        b->has_at_least_one_ref = &HasAtLeastOneRefCb;
        var t = (Trailer*)(p + offset);
        t->Count = 1;
        t->Owner = owner is null ? 0 : GCHandle.ToIntPtr(GCHandle.Alloc(owner));
        return (T*)p;
    }

    /// <summary>The .NET object a struct of ours belongs to; null once it is gone.</summary>
    public static TOwner? Owner<TOwner>(void* self) where TOwner : class
    {
        var t = TrailerOf((cef_base_ref_counted_t*)self);
        return t->Owner == 0 ? null : GCHandle.FromIntPtr(t->Owner).Target as TOwner;
    }

    private static Trailer* TrailerOf(cef_base_ref_counted_t* b) => (Trailer*)((byte*)b + TrailerOffset((int)b->size));

    /// <summary>One of ours, about to be handed to CEF as an argument or a return value.</summary>
    public static T* Give<T>(T* ours) where T : unmanaged
    {
        if (ours != null) Interlocked.Increment(ref TrailerOf((cef_base_ref_counted_t*)ours)->Count);
        return ours;
    }

    /// <summary>Gives back a reference: one of CEF's we received, or one of ours we hold.</summary>
    public static void Release<T>(T* any) where T : unmanaged
    {
        var b = (cef_base_ref_counted_t*)any;
        if (b != null) b->release(b);
    }

    /// <summary>Takes another reference to a struct CEF gave us, to keep it past the callback.</summary>
    public static T* AddRef<T>(T* any) where T : unmanaged
    {
        var b = (cef_base_ref_counted_t*)any;
        if (b != null) b->add_ref(b);
        return any;
    }

    /// <summary>The live count of one of ours, for tests.</summary>
    internal static long CountOf(void* ours) => Volatile.Read(ref TrailerOf((cef_base_ref_counted_t*)ours)->Count);

    [UnmanagedCallersOnly]
    private static void AddRefCb(cef_base_ref_counted_t* self) => Interlocked.Increment(ref TrailerOf(self)->Count);

    [UnmanagedCallersOnly]
    private static int ReleaseCb(cef_base_ref_counted_t* self)
    {
        var t = TrailerOf(self);
        if (Interlocked.Decrement(ref t->Count) != 0) return 0;
        if (t->Owner != 0) GCHandle.FromIntPtr(t->Owner).Free();
        NativeMemory.Free(self);
        return 1;
    }

    [UnmanagedCallersOnly]
    private static int HasOneRefCb(cef_base_ref_counted_t* self) => Volatile.Read(ref TrailerOf(self)->Count) == 1 ? 1 : 0;

    [UnmanagedCallersOnly]
    private static int HasAtLeastOneRefCb(cef_base_ref_counted_t* self) => Volatile.Read(ref TrailerOf(self)->Count) >= 1 ? 1 : 0;
}

/// <summary>UTF-16 <c>cef_string_t</c>s, CEF's default string type.</summary>
internal static unsafe class CefString
{
    /// <summary>Sets <paramref name="s"/> to a copy CEF owns (it frees it with the struct's dtor).</summary>
    public static void Set(cef_string_utf16_t* s, string value)
    {
        fixed (char* c = value) Capi.cef_string_utf16_set(c, (ulong)value.Length, s, 1);
    }

    /// <summary>A string to pass in; <see cref="Clear"/> it after the call.</summary>
    public static cef_string_utf16_t Make(string value)
    {
        cef_string_utf16_t s = default;
        Set(&s, value);
        return s;
    }

    public static void Clear(cef_string_utf16_t* s) => Capi.cef_string_utf16_clear(s);

    public static string Read(cef_string_utf16_t* s)
        => s == null || s->str == null ? "" : new string(s->str, 0, (int)s->length);

    /// <summary>Reads and frees a <c>cef_string_userfree_t</c>, which the caller owns.</summary>
    public static string Take(cef_string_utf16_t* userfree)
    {
        if (userfree == null) return "";
        var r = Read(userfree);
        Capi.cef_string_userfree_utf16_free(userfree);
        return r;
    }
}
