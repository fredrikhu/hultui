using HultUI;
using HultUI.Core.Reactivity;

namespace GitClient.Git;

/// <summary>
/// Loads a value on a background thread and exposes it reactively, so a
/// component's <c>Build()</c> stays cheap instead of blocking the UI thread on
/// git subprocesses.
///
/// <para>Reading <see cref="Value"/> or <see cref="IsLoading"/> inside a
/// <c>Build()</c> tracks the dependency. Call <see cref="Load"/> (from the UI
/// thread) with a key: if the key differs from the currently loaded one, the
/// producer runs on a thread-pool thread and its result is applied on the UI
/// thread — triggering a rebuild. Requests for the same key are ignored (cache
/// hit), and a newer key supersedes an in-flight older one (its stale result is
/// dropped).</para>
///
/// <para>Keying strategy is up to the caller: a commit view keys on the commit
/// hash (so re-selecting the same commit is a cache hit); a view with no natural
/// key uses a monotonic "refresh generation" that it bumps when the underlying
/// data actually changed (so re-showing the view without changes is a cache
/// hit).</para>
/// </summary>
public sealed class AsyncLoader<T>
{
    private readonly Signal<T> _value;
    private readonly Signal<bool> _loading = new(false);
    private object? _loadedKey;
    private int _generation;

    public AsyncLoader(T initial) => _value = new Signal<T>(initial);

    /// <summary>The most recently loaded value (reactive read).</summary>
    public T Value => _value.Value;

    /// <summary>True while a background load is in flight (reactive read).</summary>
    public bool IsLoading => _loading.Value;

    /// <summary>
    /// Ensures the value for <paramref name="key"/> is loaded. No-op if that key
    /// is already loaded or currently loading. Must be called on the UI thread
    /// (typically from <c>Build()</c>).
    /// </summary>
    /// <param name="produce">Runs on a thread-pool thread; must not touch UI or
    /// reactive state.</param>
    /// <param name="onLoaded">Optional; runs on the UI thread with the result
    /// once it is applied (before the resulting rebuild), for side effects such
    /// as adjusting a selection.</param>
    public void Load(object key, Func<T> produce, Action<T>? onLoaded = null)
    {
        if (Equals(key, _loadedKey))
            return;

        _loadedKey = key;
        int gen = ++_generation;
        _loading.Value = true;

        _ = Task.Run(() =>
        {
            T result;
            try
            {
                result = produce();
            }
            catch
            {
                // Clear the spinner even on failure so the UI doesn't hang in a
                // loading state; the previous value is kept.
                Application.InvokeOnMainThread(() =>
                {
                    if (gen == _generation)
                        _loading.Value = false;
                });
                return;
            }

            Application.InvokeOnMainThread(() =>
            {
                if (gen != _generation)
                    return; // superseded by a newer Load — drop the stale result

                // Batch so the value, spinner, and any onLoaded side effects
                // coalesce into a single rebuild.
                ReactiveRuntime.Batch(() =>
                {
                    _value.Value = result;
                    _loading.Value = false;
                    onLoaded?.Invoke(result);
                });
            });
        });
    }
}
