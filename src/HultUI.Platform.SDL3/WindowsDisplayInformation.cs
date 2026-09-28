using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.DevTools;

namespace HultUI.Platform.SDL3;

/// <summary>
/// WinRT's <c>Windows.Graphics.Display.DisplayInformation</c> for one window, reached through
/// hand-rolled COM: what Windows says about the window's display — HDR or not, SDR white, peak
/// — and an event the moment any of it changes.
///
/// <para><b>Why an event.</b> Dragging the SDR brightness slider sends no window message at all
/// (a subclassed window logging every sent message saw nothing), so without this the only way to
/// notice was to poll, and at a 100 ms poll the window visibly trailed the SDR windows beside it.
/// <c>AdvancedColorInfoChanged</c> fired once per 4-nit step of a drag, 4 to 16 ms apart,
/// measured with the SDK projection before any of this was written.</para>
///
/// <para><b>Why hand-rolled.</b> The projection needs a Windows target framework, which this
/// assembly — built for Linux too — cannot have, and would bring a 26 MB
/// <c>Microsoft.Windows.SDK.NET.dll</c> into every app. Four interfaces and one delegate are the
/// whole of what is used. Every number below was checked, not remembered: the IIDs came from the
/// projection's metadata, the delegate's parameterized IID from the WinRT algorithm after that
/// algorithm reproduced two published IIDs exactly, the vtable slots from the OS's own
/// <c>Windows.Graphics.winmd</c> (where method order is vtable order) and, for the interop
/// factory, from the vtable index the projection's IL uses.</para>
///
/// <para><b>What it needs</b> (<c>IDisplayInformationStaticsInterop::GetForWindow</c>): Windows
/// 11 build 22621 or later; a top-level window owned by the calling thread; and a
/// <c>Windows.System.DispatcherQueue</c> on that thread, which this creates once. Events arrive
/// on that queue, which SDL's own message pump services, so the handler runs on the UI thread.
/// Anything that fails here returns null and the caller keeps polling the display
/// configuration instead.</para>
///
/// <para><b>Opt-in</b>, through <c>Application</c>'s <c>displayColorEvents</c> or
/// <c>HULTUI_DISPLAY_INFORMATION=1</c>: an app without HDR content gains nothing from it, and it
/// hooks the window's message loop. Start-up cost when on: about 9 ms.</para>
/// </summary>
[SupportedOSPlatform("windows10.0.22621")]
internal sealed unsafe class WindowsDisplayInformation : IDisposable
{
    private static readonly Guid IID_IDisplayInformationStaticsInterop = new("7449121C-382B-4705-8DA7-A795BA482013");
    private static readonly Guid IID_IDisplayInformation = new("BED112AE-ADC3-4DC9-AE65-851F4D7D4799");
    private static readonly Guid IID_IDisplayInformation5 = new("3A5442DC-2CDE-4A8D-80D1-21DC5ADCC1AA");
    /// <summary><c>TypedEventHandler&lt;DisplayInformation, Object&gt;</c>, a parameterized
    /// IID: SHA-1 of the WinRT signature under the pinterface namespace.</summary>
    private static readonly Guid IID_ColorInfoChangedHandler = new("86C4F619-67B6-51C7-B30D-D8CF13625327");
    private static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid IID_IAgileObject = new("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90");

    // Vtable slots. IInspectable's six come first on every WinRT interface.
    private const int SlotQueryInterface = 0, SlotRelease = 2;
    private const int SlotGetForWindow = 6;                      // IDisplayInformationStaticsInterop
    private const int SlotGetAdvancedColorInfo = 6;              // IDisplayInformation5
    private const int SlotAddColorInfoChanged = 7;
    private const int SlotRemoveColorInfoChanged = 8;
    private const int SlotCurrentAdvancedColorKind = 6;          // IAdvancedColorInfo
    private const int SlotMaxLuminanceInNits = 11;
    private const int SlotSdrWhiteLevelInNits = 14;

    private const int AdvancedColorKindHighDynamicRange = 2;
    private const int S_OK = 0;
    private const int E_NOINTERFACE = unchecked((int)0x80004002);

    /// <summary>The thread's <c>DispatcherQueueController</c>, kept for the thread's lifetime:
    /// releasing it would shut the queue down under every <c>DisplayInformation</c> on it.</summary>
    [ThreadStatic] private static nint _dispatcherController;
    [ThreadStatic] private static bool _dispatcherTried;

    private nint _info5;          // IDisplayInformation5*
    private long _token;
    private Handler* _handler;

    private WindowsDisplayInformation() { }

    /// <summary>
    /// One for this window, subscribed so that <paramref name="changed"/> runs on the UI thread
    /// whenever the display's colour information changes; null when any requirement is not met.
    /// </summary>
    public static WindowsDisplayInformation? TryCreate(nint hwnd, Action changed)
    {
        if (hwnd == 0) return null;
        try
        {
            EnsureDispatcherQueue();

            nint factory = GetActivationFactory("Windows.Graphics.Display.DisplayInformation",
                IID_IDisplayInformationStaticsInterop, out int hr);
            if (factory == 0) return Fail("RoGetActivationFactory", hr);

            nint info = 0;
            var iidInfo = IID_IDisplayInformation;
            hr = ((delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)Slot(factory, SlotGetForWindow))
                (factory, hwnd, &iidInfo, &info);
            Release(factory);
            if (hr != S_OK || info == 0) return Fail("GetForWindow", hr);

            nint info5 = QueryInterface(info, IID_IDisplayInformation5, out hr);
            Release(info);
            if (info5 == 0) return Fail("QueryInterface(IDisplayInformation5)", hr);

            var result = new WindowsDisplayInformation { _info5 = info5, _handler = Handler.Create(changed) };
            long token = 0;
            hr = ((delegate* unmanaged[Stdcall]<nint, Handler*, long*, int>)Slot(info5, SlotAddColorInfoChanged))
                (info5, result._handler, &token);
            if (hr != S_OK)
            {
                result.Dispose();
                return Fail("add_AdvancedColorInfoChanged", hr);
            }
            result._token = token;
            StartupTrace.Mark("display-information: subscribed to AdvancedColorInfoChanged" +
                (result.Read() is { } now ? $" (hdr={now.Hdr} sdrWhite={now.SdrWhiteNits:0} peak={now.MaxLuminanceNits:0})" : ""));
            return result;
        }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }

        static WindowsDisplayInformation? Fail(string what, int hr)
        {
            StartupTrace.Mark($"display-information unavailable: {what} -> 0x{hr:X8}; polling instead");
            return null;
        }
    }

    /// <summary>What Windows says about the window's display now, or null if it will not say.</summary>
    public (bool Hdr, float SdrWhiteNits, float MaxLuminanceNits)? Read()
    {
        if (_info5 == 0) return null;
        nint color = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(_info5, SlotGetAdvancedColorInfo))(_info5, &color);
        if (hr != S_OK || color == 0) return null;
        try
        {
            int kind = 0;
            float white = 0f, peak = 0f;
            if (((delegate* unmanaged[Stdcall]<nint, int*, int>)Slot(color, SlotCurrentAdvancedColorKind))(color, &kind) != S_OK
                || ((delegate* unmanaged[Stdcall]<nint, float*, int>)Slot(color, SlotSdrWhiteLevelInNits))(color, &white) != S_OK
                || ((delegate* unmanaged[Stdcall]<nint, float*, int>)Slot(color, SlotMaxLuminanceInNits))(color, &peak) != S_OK)
                return null;
            return (kind == AdvancedColorKindHighDynamicRange, white, peak);
        }
        finally
        {
            Release(color);
        }
    }

    /// <summary>Unsubscribes and drops the object. Must run on the UI thread, before the window
    /// is destroyed: <c>DisplayInformation</c> hooks the window's message loop.</summary>
    public void Dispose()
    {
        if (_info5 != 0)
        {
            if (_token != 0)
                ((delegate* unmanaged[Stdcall]<nint, long, int>)Slot(_info5, SlotRemoveColorInfoChanged))(_info5, _token);
            Release(_info5);
            _info5 = 0;
            _token = 0;
        }
        if (_handler != null)
        {
            // Our own reference; the event source dropped its one on remove.
            Handler.Release(_handler);
            _handler = null;
        }
    }

    private static void EnsureDispatcherQueue()
    {
        if (_dispatcherTried) return;
        _dispatcherTried = true;
        var options = new DispatcherQueueOptions
        {
            dwSize = sizeof(DispatcherQueueOptions),
            threadType = 2,      // DQTYPE_THREAD_CURRENT: on this, the UI thread
            apartmentType = 0,   // DQTAT_COM_NONE: leave the apartment as the app set it
        };
        int hr = CreateDispatcherQueueController(options, out nint controller);
        // A thread that already has a queue — a host that made its own — answers with an error
        // here and serves GetForWindow just the same, so failure is traced and not fatal.
        if (hr == S_OK) _dispatcherController = controller;
        else StartupTrace.Mark($"display-information: CreateDispatcherQueueController -> 0x{hr:X8}");
    }

    private static nint GetActivationFactory(string className, Guid iid, out int hr)
    {
        hr = WindowsCreateString(className, (uint)className.Length, out nint hstring);
        if (hr != S_OK) return 0;
        try
        {
            hr = RoGetActivationFactory(hstring, &iid, out nint factory);
            return hr == S_OK ? factory : 0;
        }
        finally
        {
            WindowsDeleteString(hstring);
        }
    }

    private static void* Slot(nint obj, int slot) => (*(void***)obj)[slot];

    private static nint QueryInterface(nint obj, Guid iid, out int hr)
    {
        nint result = 0;
        hr = ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(obj, SlotQueryInterface))(obj, &iid, &result);
        return hr == S_OK ? result : 0;
    }

    private static void Release(nint obj) =>
        ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(obj, SlotRelease))(obj);

    /// <summary>
    /// The <c>TypedEventHandler&lt;DisplayInformation, Object&gt;</c> handed to the event: a
    /// native object whose first field is its vtable, so a pointer to it is a COM interface
    /// pointer. It answers <c>IAgileObject</c>, which tells the event source to call it directly
    /// rather than try to marshal it — it does nothing but raise a flag.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Handler
    {
        private void** _vtable;
        private int _refs;
        private nint _callback;   // GCHandle to the Action

        private static readonly void** Vtable = CreateVtable();

        private static void** CreateVtable()
        {
            var v = (void**)NativeMemory.Alloc((nuint)(4 * sizeof(void*)));
            v[0] = (delegate* unmanaged[Stdcall]<Handler*, Guid*, void**, int>)&QueryInterface;
            v[1] = (delegate* unmanaged[Stdcall]<Handler*, uint>)&AddRef;
            v[2] = (delegate* unmanaged[Stdcall]<Handler*, uint>)&ReleaseThunk;
            v[3] = (delegate* unmanaged[Stdcall]<Handler*, nint, nint, int>)&Invoke;
            return v;
        }

        public static Handler* Create(Action callback)
        {
            var h = (Handler*)NativeMemory.Alloc((nuint)sizeof(Handler));
            h->_vtable = Vtable;
            h->_refs = 1;
            h->_callback = GCHandle.ToIntPtr(GCHandle.Alloc(callback));
            return h;
        }

        public static uint Release(Handler* h)
        {
            int refs = Interlocked.Decrement(ref h->_refs);
            if (refs == 0)
            {
                GCHandle.FromIntPtr(h->_callback).Free();
                NativeMemory.Free(h);
            }
            return (uint)refs;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int QueryInterface(Handler* h, Guid* iid, void** result)
        {
            if (*iid == IID_IUnknown || *iid == IID_ColorInfoChangedHandler || *iid == IID_IAgileObject)
            {
                Interlocked.Increment(ref h->_refs);
                *result = h;
                return S_OK;
            }
            *result = null;
            return E_NOINTERFACE;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static uint AddRef(Handler* h) => (uint)Interlocked.Increment(ref h->_refs);

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static uint ReleaseThunk(Handler* h) => Release(h);

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int Invoke(Handler* h, nint sender, nint args)
        {
            // Nothing may escape an UnmanagedCallersOnly method: it would take the process down
            // from inside SDL's message pump with no managed stack to show for it.
            try { ((Action)GCHandle.FromIntPtr(h->_callback).Target!)(); }
            catch (Exception ex) { Console.Error.WriteLine($"[HultUI] display-information handler failed: {ex}"); }
            return S_OK;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int dwSize;
        public int threadType;
        public int apartmentType;
    }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint controller);

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string sourceString, uint length, out nint hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(nint hstring);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(nint activatableClassId, Guid* iid, out nint factory);
}
