using UnityEngine;

// Rewind channel for any Toggleable: records its on/off state (sparse — it changes rarely) and restores
// it WITHOUT cascading (it calls SetActive, which applies only the object's own effect). Replaces the
// old DoorChannel / LeverChannel — add this + a RewindableEntity to any Toggleable.
[RequireComponent(typeof(Toggleable))]
public sealed class ToggleableChannel : RewindChannel<bool>
{
    private Toggleable _toggleable;

    protected override void Awake()
    {
        base.Awake();
        _toggleable = GetComponent<Toggleable>();
    }

    protected override IHistory<bool> NewHistory() => new SparseHistory<bool>();

    protected override bool Read() => _toggleable.IsActive;

    // Restore silently: own effect only, no cascade — any targets are rewound by their own channels.
    protected override void Write(bool active) => _toggleable.SetActive(active);
}
