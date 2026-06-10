using UnityEngine;
using UnityEngine.Events;

public class Lever : MonoBehaviour, IInteractable
{
    public UnityEvent On;

    public UnityEvent Off;

    private bool isOn = false;

    /// <summary>Current state, captured by <see cref="LeverChannel"/>.</summary>
    public bool IsOn => isOn;

    public void Interact()
    {
        isOn = !isOn;

        if (isOn)
            On.Invoke();
        else
            Off.Invoke();
    }

    // Rewind restore: set the state WITHOUT firing events — the wired targets (doors)
    // are restored by their own channels, so re-firing would double-drive them.
    public void RestoreState(bool on) => isOn = on;
}