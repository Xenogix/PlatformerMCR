using System.Collections.Generic;
using UnityEngine;

// The Caretaker: owns the registry of rewindable entities, the capture cadence, and
// the instant jump-back. It no longer owns a clock — it rides GameClock as an OBSERVER
// (ticked BEFORE the movers each fixed tick) so it captures each tick's ENTERING state:
// the position+velocity the movers are about to act on. That snapshot is internally
// consistent, so restoring it and re-running the tick reproduces it exactly (see GameClock).
// Input + clone spawning live in the RewindDirector, which calls Rewind() and uses the
// returned target tick to seed/echo a clone. It never inspects memento contents.
public sealed class RewindCaretaker : MonoBehaviour, ITickable
{
    public static RewindCaretaker Instance { get; private set; }

    [Tooltip("Capture a snapshot every N fixed ticks. 1 = every tick (smoothest scrubbing, more memory); higher = coarser/cheaper.")]
    [SerializeField, Min(1)] private int captureRate = 1;

    [Tooltip("Sliding history window in seconds: older history is evicted and long-dead objects reclaimed. 0 = unlimited (no eviction).")]
    [SerializeField, Min(0f)] private float windowSeconds = 0f;

    private readonly List<RewindableEntity> _entities = new();
    private int _firstCapturedTick = -1;
    private bool _hasCaptured;
    private int _lastCapturedTick = int.MinValue; // dedup: never capture the same tick twice (e.g. right after a rewind)
    private int _windowTicks;  // windowSeconds in ticks (cached)

    public int CurrentTick => GameClock.HasInstance ? GameClock.Instance.Tick : 0;

    /// <summary>True once at least one snapshot has been captured (scrubbing is possible).</summary>
    public bool HasCaptured => _hasCaptured;

    /// <summary>Earliest tick still in history — the left edge of the scrub window.</summary>
    public int FirstCapturedTick => _firstCapturedTick;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        _windowTicks = GameClock.SecondsToTicks(windowSeconds);
    }

    private void OnEnable() => GameClock.Instance.RegisterObserver(this);

    private void OnDisable()
    {
        if (GameClock.HasInstance) GameClock.Instance.UnregisterObserver(this);
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

    // Observer: capture the whole world on the capture cadence, before the movers act this tick
    public void Tick(int tick, float dt)
    {
        if (tick % captureRate == 0 && tick != _lastCapturedTick)
            CaptureAll(tick);
    }

    private void CaptureAll(int tick)
    {
        if (!_hasCaptured) { _firstCapturedTick = tick; _hasCaptured = true; }
        for (int i = 0; i < _entities.Count; i++)
        {
            // Revive any dormant entity the clock has (re-)entered the lifetime of (a clone the
            // player rewound past its split point, now replayed back into its window), THEN capture.
            _entities[i].PrepareCapture(tick);
            _entities[i].Capture(tick);
        }
        _lastCapturedTick = tick;
        if (windowSeconds > 0f) EvictAndReclaim(tick);
    }

    // Slide the history window: evict entries older than it, and destroy dormant
    // entities that can no longer be a rewind target.
    private void EvictAndReclaim(int now)
    {
        int windowStart = now - _windowTicks;
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

    public int SnapToCapture(int tick)
    {
        int now = GameClock.HasInstance ? GameClock.Instance.Tick : 0;
        if (tick > now) tick = now;
        tick -= ((tick % captureRate) + captureRate) % captureRate; // snap down to a capture tick
        if (tick < _firstCapturedTick) tick = _firstCapturedTick;
        return tick;
    }

    public void Preview(int tick)
    {
        if (!_hasCaptured) return;
        tick = SnapToCapture(tick);
        for (int i = 0; i < _entities.Count; i++) _entities[i].RestoreTo(tick);
        Physics2D.SyncTransforms(); // align colliders with the restored poses while paused
    }

    public int Commit(int tick)
    {
        if (!_hasCaptured) return -1;
        tick = SnapToCapture(tick);
        for (int i = 0; i < _entities.Count; i++)
        {
            bool aliveAtTarget = _entities[i].IsAliveAt(tick);
            _entities[i].RestoreTo(tick);
            if (aliveAtTarget) _entities[i].DiscardAfter(tick);
        }
        GameClock.Instance.RewindTo(tick);
        _lastCapturedTick = tick; // target's state is already recorded; don't re-capture it next tick
        return tick;
    }
}
