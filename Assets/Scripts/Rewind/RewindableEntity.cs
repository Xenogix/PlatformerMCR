using UnityEngine;

// Identity + lifecycle coordinator for one rewindable GameObject. It holds the
// object's channels (each owns its own history) AND its existence (the "alive
// record"), presenting a single face to the RewindCaretaker.
//
// Lifecycle (deferred destruction):
//   - Despawn() records alive=false and DEACTIVATES the object — never Destroy()-ed
//     during play, so a rewind can reactivate the retained instance.
//   - Existence rule: an entity exists at tick T iff its alive record carries true at
//     T (default false before its first capture). On restore: alive -> reactivate +
//     restore channels; not alive -> deactivate.
//   - Reclamation: once an entity is dormant AND its last alive-change has aged out of
//     the window, it can never be a rewind target again, so the caretaker destroys it.
public sealed class RewindableEntity : MonoBehaviour
{
    private IRewindChannel[] _channels;
    private readonly SparseHistory<bool> _alive = new();
    private bool _registered;
    private bool _dormant;

    private void Awake()
    {
        _channels = GetComponentsInChildren<IRewindChannel>(includeInactive: true);
    }

    private void Start()
    {
        if (!_registered && RewindCaretaker.Instance != null)
        {
            RewindCaretaker.Instance.Register(this);
            _registered = true;
        }
    }

    private void OnDestroy()
    {
        if (_registered && RewindCaretaker.Instance != null)
            RewindCaretaker.Instance.Unregister(this);
    }

    public void Capture(int tick)
    {
        if (_dormant) return;                 // despawned: nothing to record
        _alive.Record(tick, true);            // sparse-suppressed -> stored once at birth
        for (int i = 0; i < _channels.Length; i++) _channels[i].Capture(tick);
    }

    public void RestoreTo(int tick)
    {
        bool aliveAtTick = _alive.ValueAtOr(tick, false);
        if (aliveAtTick)
        {
            if (_dormant) { gameObject.SetActive(true); _dormant = false; }
            for (int i = 0; i < _channels.Length; i++) _channels[i].Restore(tick);
        }
        else if (!_dormant)
        {
            gameObject.SetActive(false);
            _dormant = true;
        }
    }

    public bool IsAliveAt(int tick) => _alive.ValueAtOr(tick, false);

    public void PrepareCapture(int tick)
    {
        if (!_dormant || !_alive.ValueAtOr(tick, false)) return;
        for (int i = 0; i < _channels.Length; i++) _channels[i].Restore(tick);
        gameObject.SetActive(true);
        _dormant = false;
        for (int i = 0; i < _channels.Length; i++) _channels[i].Clear();
    }

    // Gameplay "despawn": deactivate and record the transition; never Destroy.
    public void Despawn()
    {
        if (_dormant) return;
        int tick = RewindCaretaker.Instance != null ? RewindCaretaker.Instance.CurrentTick : 0;
        _alive.Record(tick, false);
        _dormant = true;
        gameObject.SetActive(false);
    }

    public void DiscardAfter(int tick)
    {
        _alive.DiscardAfter(tick);
        for (int i = 0; i < _channels.Length; i++) _channels[i].DiscardAfter(tick);
    }

    public void TrimBefore(int windowStartTick)
    {
        _alive.TrimBefore(windowStartTick);
        for (int i = 0; i < _channels.Length; i++) _channels[i].TrimBefore(windowStartTick);
    }

    // Reclaimable once dormant and the last alive-change is at/before the window start
    // (no in-window tick could ever revive it).
    public bool CanReclaim(int windowStart) => _dormant && _alive.LastTick <= windowStart;

    public void Reclaim()
    {
        _registered = false; // caretaker has already removed us from its registry
        Destroy(gameObject);
    }
}
