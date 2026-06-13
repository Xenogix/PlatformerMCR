using UnityEngine;
using UnityEngine.Events;

public class Lever : MonoBehaviour, IInteractable
{
    public UnityEvent On;

    public UnityEvent Off;

    [Tooltip("Transform flipped 180° about its local Y while the lever is on. Defaults to this object's transform.")]
    [SerializeField] private Transform pivot;

    private bool isOn = false;
    private Quaternion baseRotation; // the 'off' pose, captured in Awake; 'on' is this + 180° about Y

    /// <summary>Current state, captured by <see cref="LeverChannel"/>.</summary>
    public bool IsOn => isOn;

    private void Awake()
    {
        if (pivot == null) pivot = transform;
        baseRotation = pivot.localRotation;
        ApplyRotation();
    }

    public void Interact()
    {
        isOn = !isOn;
        ApplyRotation();

        if (isOn)
            On.Invoke();
        else
            Off.Invoke();
    }

    // Rewind restore: set the state WITHOUT firing events — the wired targets (doors)
    // are restored by their own channels, so re-firing would double-drive them. The handle
    // itself is on no other channel, so we re-apply its rotation here to match the restored state.
    public void RestoreState(bool on)
    {
        isOn = on;
        ApplyRotation();
    }

    // The handle sits at its base ('off') rotation, flipped 180° about local Y when on. An instant
    // +180° and -180° land on the same orientation, so anchoring to baseRotation keeps the pose exact
    // (no drift from repeated deltas) and lets a rewind snap straight to the correct handle position.
    private void ApplyRotation()
    {
        pivot.localRotation = baseRotation * Quaternion.Euler(0f, isOn ? 180f : 0f, 0f);
    }
}
