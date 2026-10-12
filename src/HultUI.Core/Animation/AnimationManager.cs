using HultUI.Core.Threading;

namespace HultUI.Core.Animation;

public static class AnimationManager
{
    private static readonly List<ITween> _active = [];
    private static readonly List<ITween> _pendingAdd = [];
    private static bool _ticking;

    public static bool HasActiveAnimations => _active.Count > 0 || _pendingAdd.Count > 0;

    // Whether a tween was added since the last step (or during it): it has its first step to take.
    private static bool _freshAdded;

    /// <summary>Whether a tween has not had its first step yet: the frame loop steps it with the
    /// next frame it may draw, whatever else it waits for.</summary>
    internal static bool HasFreshTweens => _freshAdded || _pendingAdd.Count > 0;

    /// <summary>
    /// Seconds from the last step until a step can next change anything, the earliest of every
    /// active tween's: 0 when one is running or has not had its first step, what is left of the
    /// shortest delay when they all wait one out (a toast's hold, a tooltip's show delay, a
    /// player's auto-hide), and <see cref="float.PositiveInfinity"/> with none. The frame loop
    /// steps again then, and not every frame interval in between.
    /// </summary>
    internal static float NextChangeIn
    {
        get
        {
            if (_pendingAdd.Count > 0) return 0f;
            float soonest = float.PositiveInfinity;
            for (int i = 0; i < _active.Count; i++)
            {
                var tween = _active[i];
                if (tween.IsComplete) continue;
                float next = tween.NextChangeIn;
                if (next <= 0f) return 0f;
                if (next < soonest) soonest = next;
            }
            return soonest;
        }
    }

    internal static void Add(ITween tween)
    {
        if (_ticking)
            _pendingAdd.Add(tween);
        else
            _active.Add(tween);
        _freshAdded = true;
        // A tween started inside the frame loop's wait (an OLE drop, a WebView2 event) has its
        // first step due now, and nothing else may wake the loop for it.
        UIThread.WakeIfParked();
    }

    internal static void Remove(ITween tween)
    {
        tween.Cancel();
    }

    /// <summary>
    /// Cancels and drops every active and pending tween. Animation state is
    /// process-global; headless test sessions call this on teardown so tweens
    /// started in one test can never tick into the next.
    /// </summary>
    internal static void Reset()
    {
        foreach (var tween in _active) tween.Cancel();
        foreach (var tween in _pendingAdd) tween.Cancel();
        _active.Clear();
        _pendingAdd.Clear();
        _ticking = false;
        _freshAdded = false;
    }

    /// <summary>Steps every active tween by <paramref name="deltaSeconds"/>, one that has not
    /// been stepped yet included.</summary>
    public static void Tick(float deltaSeconds) => Tick(deltaSeconds, deltaSeconds);

    /// <summary>
    /// Steps every active tween by <paramref name="deltaSeconds"/>, and one that has not been
    /// stepped yet by <paramref name="firstStepSeconds"/>. The frame loop passes 0: a tween
    /// started between frames starts its clock at its first step, so one started after the
    /// loop was idle — it waits for its next deadline, which can be minutes away — is not
    /// stepped by all of that and does not jump to its end; its frames come at most an
    /// interval later than its start. A host or a test that steps by the time since the last
    /// step, a tween's start included, uses <see cref="Tick(float)"/>.
    /// </summary>
    internal static void Tick(float deltaSeconds, float firstStepSeconds)
    {
        _freshAdded = false;
        if (_active.Count == 0 && _pendingAdd.Count == 0)
            return;

        if (_pendingAdd.Count > 0)
        {
            _active.AddRange(_pendingAdd);
            _pendingAdd.Clear();
        }

        _ticking = true;
        for (int i = 0; i < _active.Count; i++)
        {
            var tween = _active[i];
            tween.Tick(tween.Started ? deltaSeconds : firstStepSeconds);
        }
        _ticking = false;

        for (int i = _active.Count - 1; i >= 0; i--)
        {
            if (_active[i].IsComplete)
                _active.RemoveAt(i);
        }

        // Added by a tween's step (a completion that chains the next): first steps to come, which
        // Add has marked (_freshAdded).
        if (_pendingAdd.Count > 0)
        {
            _active.AddRange(_pendingAdd);
            _pendingAdd.Clear();
        }
    }
}
