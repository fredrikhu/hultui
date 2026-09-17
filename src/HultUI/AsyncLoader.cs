using HultUI.Core.Reactivity;

namespace HultUI;

/// <summary>
/// Loads a value on a background thread and exposes it reactively, so a
/// component's <c>Build()</c> stays cheap instead of blocking the UI thread on
/// slow work (file/network I/O, subprocesses, heavy computation).
/// </summary>
/// <remarks>
/// <para>Reading <see cref="Value"/> or <see cref="IsLoading"/> inside a
/// <c>Build()</c> tracks the dependency. Call <see cref="Load"/> (from the UI
/// thread) with a key: if the key differs from the currently loaded one, the
/// producer runs on a thread-pool thread and its result is applied on the UI
/// thread via <see cref="Application.InvokeOnMainThread"/> — triggering a
/// rebuild. Requests for the same key are ignored (cache hit), and a newer key
/// supersedes an in-flight older one (its stale result is dropped).</para>
///
/// <para><b>Failure.</b> A producer that throws leaves the previous value in place
/// and retries, up to <see cref="MaxAttempts"/> times with a widening delay. The key
/// counts as loaded only once a value has actually been <i>delivered</i>: a load that
/// threw used to record its key anyway, so every later <c>Load</c> for it was a cache
/// hit and the loader sat on its seed value for the life of the process. In Junction
/// that showed as a repository stuck on "…" with no branch, no counts and no changes,
/// hours after a <c>git pull</c> whose half-written index had refused one status call —
/// and because nothing re-keys a repository whose working tree and remote have both gone
/// quiet, there was no way back short of restarting the app.</para>
///
/// <para>The result is applied inside a <see cref="ReactiveRuntime.Batch"/>, so
/// the value, the loading flag, and any <c>onLoaded</c> side effects coalesce
/// into a single rebuild.</para>
///
/// <para><b>Keying.</b> The key strategy is the caller's choice. A view that
/// renders a specific entity keys on that entity's id (so re-selecting it is a
/// cache hit). A view with no natural key uses a monotonic "refresh generation"
/// counter that it bumps only when the underlying data actually changed — so a
/// plain re-render (selection, hover, layout toggle) is a cache hit and does not
/// re-run the load. Keep those UI-only rebuilds on <c>MarkDirty()</c> and reserve
/// the generation bump for real data changes.</para>
///
/// <para><b>Threading.</b> The <c>produce</c> delegate runs off the UI thread and
/// must not touch elements or reactive state. Everything else — <see cref="Load"/>,
/// the reads, and <c>onLoaded</c> — is UI-thread only. Under a headless session the
/// hop is deterministic instead of pooled — the producer runs during the next
/// <c>PumpFrame</c> and the result still lands a frame after the <see cref="Load"/>
/// (see <see cref="AsyncWork"/>).</para>
///
/// <example>
/// <code>
/// private readonly AsyncLoader&lt;IReadOnlyList&lt;Item&gt;&gt; _items = new([]);
/// private int _gen;
///
/// public override Element Build()
/// {
///     _items.Load(_gen, () =&gt; _service.LoadItems());   // off-thread
///     return ItemList(_items.Value, _items.IsLoading);  // cheap render
/// }
///
/// public void Reload() { _gen++; MarkDirty(); }         // data changed
/// </code>
/// </example>
/// </remarks>
public sealed class AsyncLoader<T>
{
    /// <summary>How many times a failed load is retried before the loader gives up and
    /// waits for a new key. Bounded, because a producer that fails the same way four
    /// times running is not losing a race — it is broken, and a client that keeps
    /// relaunching subprocesses for it is worse than a stale value. The delays widen
    /// 0.5 s · 1.5 s · 4.5 s, long enough for the writer that refused it to finish.</summary>
    private const int MaxAttempts = 3;

    private readonly Signal<T> _value;
    private readonly Signal<bool> _loading = new(false);
    /// <summary>The key whose value <see cref="_value"/> actually holds. Advanced on
    /// delivery, never on the attempt — see the Failure note on the class.</summary>
    private object? _loadedKey;
    /// <summary>The key currently in flight, waiting to be retried, or given up on.
    /// Keeps a rebuild from restarting a load that is already under way, and — once the
    /// attempts are spent — from spinning on one that cannot succeed.</summary>
    private object? _attemptedKey;
    private int _generation;

    /// <summary>Creates a loader seeded with an initial value shown until the first load lands.</summary>
    public AsyncLoader(T initial) => _value = new Signal<T>(initial);

    /// <summary>The most recently loaded value (reactive read).</summary>
    public T Value => _value.Value;

    /// <summary>True while a background load is in flight (reactive read).</summary>
    public bool IsLoading => _loading.Value;

    /// <summary>
    /// Ensures the value for <paramref name="key"/> is loaded. No-op if that key is
    /// already loaded, currently loading, or waiting out a retry. Must be called on the
    /// UI thread (typically from <c>Build()</c>).
    /// </summary>
    /// <param name="key">Identifies the desired value. Equal keys are a cache hit.</param>
    /// <param name="produce">Runs on a thread-pool thread; must not touch UI or reactive state.</param>
    /// <param name="onLoaded">Optional; runs on the UI thread with the result once
    /// it is applied (before the resulting rebuild), for side effects such as
    /// adjusting a selection.</param>
    public void Load(object key, Func<T> produce, Action<T>? onLoaded = null)
    {
        // Two different "already handled" answers: the value is here, or an attempt at it
        // is. The second is what keeps the rebuild that a failure itself triggers (the
        // loading flag falling is a reactive write) from starting the load again.
        if (Equals(key, _loadedKey) || Equals(key, _attemptedKey))
            return;

        _attemptedKey = key;
        Start(key, produce, onLoaded, attempt: 0);
    }

    private void Start(object key, Func<T> produce, Action<T>? onLoaded, int attempt)
    {
        int gen = ++_generation;
        _loading.Value = true;

        AsyncWork.Schedule(() =>
        {
            T result;
            try
            {
                result = produce();
            }
            catch
            {
                Application.InvokeOnMainThread(() =>
                {
                    if (gen != _generation)
                        return; // superseded by a newer Load — it owns the retries now

                    // Clear the spinner even on failure so the UI doesn't hang in a
                    // loading state; the previous value is kept.
                    _loading.Value = false;
                    if (attempt < MaxAttempts)
                        RetryAfter(key, produce, onLoaded, attempt, gen);
                });
                return;
            }

            Application.InvokeOnMainThread(() =>
            {
                if (gen != _generation)
                    return; // superseded by a newer Load — drop the stale result

                ReactiveRuntime.Batch(() =>
                {
                    _value.Value = result;
                    _loadedKey = key;
                    _loading.Value = false;
                    onLoaded?.Invoke(result);
                });
            });
        });
    }

    /// <summary>Waits out the backoff and tries the same key again, unless a newer
    /// <see cref="Load"/> has taken over meanwhile. The delay is wall-clock rather than
    /// frame-driven, so a headless session must settle over real time to see a retry —
    /// the first attempt still lands deterministically, which is what tests key on.</summary>
    private void RetryAfter(object key, Func<T> produce, Action<T>? onLoaded, int attempt, int gen)
    {
        var delay = TimeSpan.FromMilliseconds(500 * Math.Pow(3, attempt));
        _ = Task.Delay(delay).ContinueWith(_ => Application.InvokeOnMainThread(() =>
        {
            if (gen != _generation) return;
            Start(key, produce, onLoaded, attempt + 1);
        }));
    }
}
