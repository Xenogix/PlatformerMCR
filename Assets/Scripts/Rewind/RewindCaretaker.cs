using System.Collections.Generic;
using UnityEngine;

// The Caretaker: owns the registry of rewindable entities, the capture cadence, and
// the instant jump-back. It no longer owns a clock — it rides GameClock as a POST-tick
// observer (ticked after the movers each fixed step) so it captures the post-move state
// of each tick. Input + clone spawning live in the RewindDirector, which calls Rewind()
// and uses the returned target tick to seed/echo a clone. It never inspects memento
// contents.
public sealed class RewindCaretaker : MonoBehaviour, ITickable
{
    public static RewindCaretaker Instance { get; private set; }

    [Tooltip("Capture a snapshot every N fixed ticks (~0.1s at 50Hz => 5).")]
    [SerializeField, Min(1)] private int captureEveryNSteps = 5;

    [Tooltip("How far back a single rewind jumps, in seconds.")]
    [SerializeField, Min(0f)] private float rewindOffsetSeconds = 3f;

    [Tooltip("Sliding history window in seconds: older history is evicted and long-dead objects reclaimed. 0 = unlimited (no eviction).")]
    [SerializeField, Min(0f)] private float windowSeconds = 0f;

    private readonly List<RewindableEntity> _entities = new();
    private int _firstCapturedTick = -1;
    private bool _hasCaptured;
    private int _lastCapturedTick = int.MinValue; // dedup: never capture the same tick twice (e.g. right after a rewind)

    public int CurrentTick => GameClock.HasInstance ? GameClock.Instance.Tick : 0;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnEnable() => GameClock.Instance.RegisterPost(this);

    private void OnDisable()
    {
        if (GameClock.HasInstance) GameClock.Instance.UnregisterPost(this);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Register(RewindableEntity e)
    {
        if (!_entities.Contains(e)) _entities.Add(e);
    }

    public void Unregister(RewindableEntity e) => _entities.Remove(e);

    // Post-tick: capture the whole world on the capture cadence, after the movers moved.
    public void Tick(int tick, float dt)
    {
        if (tick % captureEveryNSteps == 0 && tick != _lastCapturedTick)
            CaptureAll(tick);
    }

    private void CaptureAll(int tick)
    {
        if (!_hasCaptured) { _firstCapturedTick = tick; _hasCaptured = true; }
        for (int i = 0; i < _entities.Count; i++) _entities[i].Capture(tick);
        _lastCapturedTick = tick;
        if (windowSeconds > 0f) EvictAndReclaim(tick);
    }

    // Slide the history window: evict entries older than it, and destroy dormant
    // entities that can no longer be a rewind target.
    private void EvictAndReclaim(int now)
    {
        int windowTicks = Mathf.Max(1, Mathf.RoundToInt(windowSeconds / Time.fixedDeltaTime));
        int windowStart = now - windowTicks;
        if (windowStart <= _firstCapturedTick) return;
        for (int i = _entities.Count - 1; i >= 0; i--)
        {
            var e = _entities[i];
            e.TrimBefore(windowStart);
            if (e.CanReclaim(windowStart))
            {
                _entities.RemoveAt(i);
                e.Reclaim();
            }
        }
        // Advance the earliest rewindable tick so Rewind() can't clamp a target onto history
        // that has just been evicted.
        _firstCapturedTick = windowStart;
    }

    /// <summary>
    /// Jump the world back by rewindOffsetSeconds (snapped down to a capture tick, clamped
    /// to the first captured tick) and return the resolved target tick — or -1 if nothing
    /// has been captured yet. The Director uses the target to seed/echo a clone.
    /// </summary>
    public int Rewind()
    {
        if (!_hasCaptured) return -1;
        int now = GameClock.Instance.Tick;
        int offsetTicks = Mathf.Max(1, Mathf.RoundToInt(rewindOffsetSeconds / Time.fixedDeltaTime));
        int target = now - offsetTicks;
        target -= ((target % captureEveryNSteps) + captureEveryNSteps) % captureEveryNSteps; // snap down to a capture tick
        if (target < _firstCapturedTick) target = _firstCapturedTick;
        RewindTo(target);
        return target;
    }

    public void RewindTo(int target)
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            _entities[i].RestoreTo(target);
            _entities[i].DiscardAfter(target);
        }
        GameClock.Instance.RewindTo(target);
        _lastCapturedTick = target; // target's state is already recorded; don't re-capture it next step
    }
}
