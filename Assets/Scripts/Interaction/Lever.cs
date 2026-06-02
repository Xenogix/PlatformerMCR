using System;
using UnityEngine;

/// <summary>
/// Example <see cref="IUsable"/>: a lever that toggles on each Use.
///
/// Detection mirrors the existing <c>FinishFlag</c> convention — a trigger collider
/// plus <c>GetComponentInParent&lt;Player&gt;()</c> — so it works for the live player
/// and for clones with no extra code: whoever's collider is overlapping registers
/// itself as that player's "current usable".
///
/// Subscribe to <see cref="OnToggled"/> from a door/platform to react to the state.
/// </summary>
[RequireComponent(typeof(Collider))]
public class Lever : MonoBehaviour, IUsable
{
    [SerializeField] private bool isOn;

    /// <summary>Raised with the new state every time the lever is toggled.</summary>
    public event Action<bool> OnToggled;

    public bool IsOn => isOn;

    // Called by Unity when the component is first added: make the collider a trigger.
    private void Reset() => GetComponent<Collider>().isTrigger = true;

    public void Use(Player user)
    {
        isOn = !isOn;
        OnToggled?.Invoke(isOn);
    }

    private void OnTriggerEnter(Collider other)
    {
        Player player = other.GetComponentInParent<Player>();
        if (player != null) player.SetUsable(this);
    }

    private void OnTriggerExit(Collider other)
    {
        Player player = other.GetComponentInParent<Player>();
        if (player != null) player.ClearUsable(this);
    }
}
