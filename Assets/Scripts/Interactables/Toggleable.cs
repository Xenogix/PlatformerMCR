using UnityEngine;

// A switchable object — doors, spike hazards, even levers derive from this. Drop it on any prefab, set
// its default in `startActive`, and a Lever flips it. "Active" means the object's renderers AND colliders
// are ENABLED (present / solid / harmful); inactive = hidden + non-blocking.
//
// Rewindable: pair with a RewindableEntity + ToggleableChannel (sparse bool) and the on/off state is
// captured and restored like any other rewindable. SetActive() applies ONLY this object's own effect
// (no cascade), so a channel can restore it without re-driving anything — see Lever for the cascade.
public class Toggleable : MonoBehaviour
{
    [Tooltip("State at level start. true = present (renderers + colliders on); false = gone (hidden + passable).")]
    [SerializeField] private bool startActive = true;

    /// <summary>Current state. true = renderers + colliders enabled.</summary>
    public bool IsActive { get; private set; }

    private Renderer[] _renderers;
    private Collider2D[] _colliders;
    private bool _cached;

    protected virtual void Awake()
    {
        IsActive = startActive;
        ApplyState(IsActive);
    }

    /// <summary>Set the state and apply this object's OWN effect only — no side effects on other objects,
    /// so a ToggleableChannel can call this on rewind-restore without re-driving anything (e.g. a lever's
    /// doors restore via their own channels).</summary>
    public void SetActive(bool active)
    {
        IsActive = active;
        ApplyState(active);
    }

    public virtual void Toggle() => SetActive(!IsActive);

    /// <summary>The visible/physical effect of the state. Default: show + solidify when active, hide +
    /// pass-through when not. Lazy-cached, so it's safe even if invoked before Awake (no NRE).</summary>
    protected virtual void ApplyState(bool active)
    {
        if (!_cached)
        {
            _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
            _colliders = GetComponentsInChildren<Collider2D>(includeInactive: true);
            _cached = true;
        }
        for (int i = 0; i < _renderers.Length; i++)
            if (_renderers[i] != null) _renderers[i].enabled = active;
        for (int i = 0; i < _colliders.Length; i++)
            if (_colliders[i] != null) _colliders[i].enabled = active;
    }
}
