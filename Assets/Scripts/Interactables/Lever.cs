using System.Collections.Generic;
using UnityEngine;

// A lever the player (or a replaying clone) flips. It IS a Toggleable — its own on/off state is captured
// for rewind by a ToggleableChannel like any other object — and flipping it cascades to its target
// Toggleables: doors, spikes, even other levers. Direct references, no UnityEvent wiring.
public sealed class Lever : Toggleable, IInteractable
{
    [Tooltip("What this lever flips when used. Doors, spike hazards — even other levers.")]
    [SerializeField] private Toggleable[] targets;

    [Tooltip("Transform flipped 180° about its local Y while the lever is on. Defaults to this object's transform.")]
    [SerializeField] private Transform pivot;

    private Quaternion baseRotation; // the 'off' handle pose, captured before the first ApplyState
    private bool pivotReady;

    // Re-entrancy guard: a lever may target another lever, so a cycle (A→B→A) would recurse forever.
    // One shared set is fine — a cascade runs synchronously within a single tick.
    private static readonly HashSet<Lever> cascading = new();

    protected override void Awake()
    {
        EnsurePivot();
        base.Awake(); // sets IsActive = startActive and calls ApplyState (the handle pose)
    }

    // Player / clone interaction: flip ourselves AND drive our targets.
    public void Interact() => Toggle();

    public override void Toggle()
    {
        if (!cascading.Add(this)) return;       // already flipped in this cascade → cycle: stop
        bool root = cascading.Count == 1;
        try
        {
            base.Toggle();                      // flip own state + handle (ApplyState) — NO cascade
            if (targets != null)
                foreach (Toggleable t in targets)
                    if (t != null) t.Toggle();  // a door/spike just flips; another lever cascades on
        }
        finally { if (root) cascading.Clear(); }
    }

    // Own effect only — rotate the handle to match state. Called by SetActive too (incl. rewind restore),
    // so it must NOT cascade: the targets restore themselves via their own channels. Anchoring to
    // baseRotation keeps the pose exact (no drift) and lets a rewind snap straight to it.
    protected override void ApplyState(bool active)
    {
        EnsurePivot();
        pivot.localRotation = baseRotation * Quaternion.Euler(0f, active ? 180f : 0f, 0f);
    }

    private void EnsurePivot()
    {
        if (pivotReady) return;
        if (pivot == null) pivot = transform;
        baseRotation = pivot.localRotation;
        pivotReady = true;
    }
}
