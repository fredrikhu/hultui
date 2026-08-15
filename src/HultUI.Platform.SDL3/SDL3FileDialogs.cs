using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using HultUI.Core.Platform;
using SDL;
using static SDL.SDL3;

namespace HultUI.Platform.SDL3;

/// <summary>
/// The OS file dialogs, via SDL3's own implementation — the real Windows/macOS/GTK/portal
/// dialogs, parented to the window, with no external process to find and no dependency on
/// pwsh or zenity being installed.
///
/// <para>SDL's dialogs are callback-based and asynchronous, and the callback may arrive on
/// a thread SDL chose. Each pending call therefore keeps its state in a table keyed by an
/// integer handed through <c>userdata</c>, so the native callback can be a static
/// <see cref="UnmanagedCallersOnlyAttribute"/> method with nothing captured — which is
/// also what keeps this working under NativeAOT.</para>
///
/// <para>The completion source runs its continuations asynchronously on purpose. Awaiting
/// UI-thread code captured the UI synchronization context, so the continuation is posted
/// back there rather than running inline on whatever thread SDL called us from — which
/// matters now that touching reactive state off the UI thread throws.</para>
/// </summary>
public sealed class SDL3FileDialogs : IFileDialogs
{
    private readonly SDL3Window _window;

    public SDL3FileDialogs(SDL3Window window) => _window = window;

    /// <summary>
    /// SDL always has a dialog implementation; whether the running desktop can service it
    /// only shows up when one is opened, and that surfaces as a cancelled result.
    /// </summary>
    public bool IsAvailable => true;

    // ──── Pending-call table ────
    //
    // The native callback carries one pointer-sized userdata and cannot capture, so
    // pending calls live here under an integer key. Entries are removed by the callback
    // (or by the launcher when SDL never calls back), so the table stays empty at rest.

    private static readonly ConcurrentDictionary<nint, TaskCompletionSource<string[]>> Pending = new();
    private static int _nextId;

    public Task<string?> OpenFolderAsync(string? initialDirectory = null,
        CancellationToken cancellationToken = default) =>
        Single(Show(DialogKind.Folder, initialDirectory, null, allowMany: false), cancellationToken);

    public Task<string?> OpenFileAsync(string? initialDirectory = null,
        IReadOnlyList<FileDialogFilter>? filters = null,
        CancellationToken cancellationToken = default) =>
        Single(Show(DialogKind.OpenFile, initialDirectory, filters, allowMany: false), cancellationToken);

    public async Task<IReadOnlyList<string>> OpenFilesAsync(string? initialDirectory = null,
        IReadOnlyList<FileDialogFilter>? filters = null,
        CancellationToken cancellationToken = default)
    {
        var task = Show(DialogKind.OpenFile, initialDirectory, filters, allowMany: true);
        return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<string?> SaveFileAsync(string suggestedFileName, string? initialDirectory = null,
        IReadOnlyList<FileDialogFilter>? filters = null,
        CancellationToken cancellationToken = default)
    {
        // SDL takes one "default location". Given a directory it starts there; given a
        // path it also pre-fills the name, which is how the suggested name gets through.
        string? location = string.IsNullOrWhiteSpace(initialDirectory)
            ? suggestedFileName
            : Path.Combine(initialDirectory, suggestedFileName);

        return Single(Show(DialogKind.SaveFile, location, filters, allowMany: false), cancellationToken);
    }

    private static async Task<string?> Single(Task<string[]> task, CancellationToken cancellationToken)
    {
        var paths = await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return paths.Length > 0 ? paths[0] : null;
    }

    private enum DialogKind { OpenFile, SaveFile, Folder }

    private unsafe Task<string[]> Show(DialogKind kind, string? location,
        IReadOnlyList<FileDialogFilter>? filters, bool allowMany)
    {
        var completion = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        nint id = Interlocked.Increment(ref _nextId);
        Pending[id] = completion;

        // Everything native below is pinned for the duration of the SDL call only: SDL
        // copies the filters and the location string before returning.
        var locationBytes = location is null ? null : Utf8(NormalizeLocation(kind, location));
        var filterNames = new List<byte[]>();
        var filterPatterns = new List<byte[]>();
        foreach (var filter in filters ?? [])
        {
            filterNames.Add(Utf8(filter.Label));
            filterPatterns.Add(Utf8(filter.Pattern));
        }

        try
        {
            var native = stackalloc SDL_DialogFileFilter[filterNames.Count == 0 ? 1 : filterNames.Count];
            var pins = new List<GCHandle>(filterNames.Count * 2);
            try
            {
                for (int i = 0; i < filterNames.Count; i++)
                {
                    var namePin = GCHandle.Alloc(filterNames[i], GCHandleType.Pinned);
                    var patternPin = GCHandle.Alloc(filterPatterns[i], GCHandleType.Pinned);
                    pins.Add(namePin);
                    pins.Add(patternPin);
                    native[i].name = (byte*)namePin.AddrOfPinnedObject();
                    native[i].pattern = (byte*)patternPin.AddrOfPinnedObject();
                }

                fixed (byte* locationPtr = locationBytes)
                {
                    var window = _window.NativeWindow;
                    delegate* unmanaged[Cdecl]<nint, byte**, int, void> callback = &OnDialogResult;
                    int count = filterNames.Count;

                    switch (kind)
                    {
                        case DialogKind.Folder:
                            SDL_ShowOpenFolderDialog(callback, id, window, locationPtr, allowMany);
                            break;
                        case DialogKind.OpenFile:
                            SDL_ShowOpenFileDialog(callback, id, window,
                                count == 0 ? null : native, count, locationPtr, allowMany);
                            break;
                        case DialogKind.SaveFile:
                            SDL_ShowSaveFileDialog(callback, id, window,
                                count == 0 ? null : native, count, locationPtr);
                            break;
                    }
                }
            }
            finally
            {
                foreach (var pin in pins)
                    pin.Free();
            }
        }
        catch
        {
            // SDL never took ownership, so nothing will ever complete this entry.
            Pending.TryRemove(id, out _);
            throw;
        }

        return completion.Task;
    }

    /// <summary>
    /// SDL wants a directory for the open dialogs. A file path would be taken literally and
    /// the dialog would open somewhere unhelpful, so trim to the containing directory —
    /// callers hand us "the last thing the user touched" and expect the sensible reading.
    /// The save dialog is the exception: there the file part is the suggested name.
    /// </summary>
    private static string NormalizeLocation(DialogKind kind, string location)
    {
        if (kind == DialogKind.SaveFile) return location;

        try
        {
            if (Directory.Exists(location)) return Path.GetFullPath(location);
            var parent = Path.GetDirectoryName(location);
            return !string.IsNullOrEmpty(parent) && Directory.Exists(parent)
                ? Path.GetFullPath(parent)
                : location;
        }
        catch
        {
            return location;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static unsafe void OnDialogResult(nint userdata, byte** fileList, int filter)
    {
        if (!Pending.TryRemove(userdata, out var completion)) return;

        // A null list is an SDL-side error, and an empty one is the user cancelling.
        // Neither is exceptional for a caller who only asked "did I get a path?", so both
        // resolve to no paths rather than a fault.
        if (fileList is null)
        {
            completion.TrySetResult([]);
            return;
        }

        var paths = new List<string>();
        for (byte** entry = fileList; *entry is not null; entry++)
        {
            var path = Marshal.PtrToStringUTF8((nint)(*entry));
            if (!string.IsNullOrEmpty(path))
                paths.Add(path);
        }

        completion.TrySetResult(paths.ToArray());
    }

    private static byte[] Utf8(string value) => SDL3Window.MarshalUtf8(value);
}
