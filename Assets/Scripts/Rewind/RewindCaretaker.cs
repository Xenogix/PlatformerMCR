using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// The Caretaker: owns the master fixed-step tick, the registry of entities, the
// capture loop, and the instant jump-back. It never inspects memento contents.
public sealed class RewindCaretaker : MonoBehaviour
{
    public static RewindCaretaker Instance { get; private set; }

    [Tooltip("Capture a dense snapshot every N physics steps (~0.1s at 50Hz => 5).")]
    [SerializeField, Min(1)] private int captureEveryNSteps = 5;

    [Tooltip("How far back a single rewind jumps, in seconds.")]
    [SerializeField, Min(0f)] private float rewindOffsetSeconds = 3f;

    [Tooltip("Key that triggers the instant jump-back.")]
    [SerializeField] private Key rewindKey = Key.R;

    [Tooltip("Sliding history window in seconds: older history is evicted and long-dead objects reclaimed. 0 = unlimited (no eviction).")]
    [SerializeField, Min(0f)] private float windowSeconds = 0f;

    private readonly List<RewindableEntity> _entities = new();
    private int _tick;
    private int _firstCapturedTick = -1;
    private bool _hasCaptured;
    private int _lastCapturedTick = int.MinValue; // dedup: never capture the same tick twice (e.g. right after a rewind)

    public int CurrentTick => _tick;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
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

    private void FixedUpdate()
    {
        if (_tick % captureEveryNSteps == 0 && _tick != _lastCapturedTick)
            CaptureAll();
        _tick++;
    }

    private void CaptureAll()
    {
        if (!_hasCaptured) { _firstCapturedTick = _tick; _hasCaptured = true; }
        for (int i = 0; i < _entities.Count; i++) _entities[i].Capture(_tick);
        _lastCapturedTick = _tick;
        if (windowSeconds > 0f) EvictAndReclaim();
    }

    // Slide the history window: evict entries older than it, and destroy dormant
    // entities that can no longer be a rewind target.
    private void EvictAndReclaim()
    {
        int windowTicks = Mathf.Max(1, Mathf.RoundToInt(windowSeconds / Time.fixedDeltaTime));
        int windowStart = _tick - windowTicks;
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
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb[rewindKey].wasPressedThisFrame)
            RequestRewind();
    }

    public void RequestRewind()
    {
        if (!_hasCaptured) return;
        int offsetTicks = Mathf.RoundToInt(rewindOffsetSeconds / Time.fixedDeltaTime);
        int target = _tick - offsetTicks;
        target -= ((target % captureEveryNSteps) + captureEveryNSteps) % captureEveryNSteps; // snap down to a capture tick
        if (target < _firstCapturedTick) target = _firstCapturedTick;
        RewindTo(target);
    }

    public void RewindTo(int target)
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            _entities[i].RestoreTo(target);
            _entities[i].DiscardAfter(target);
        }
        _tick = target;
        _lastCapturedTick = target; // target's state is already recorded; don't re-capture it next step
    }
}
