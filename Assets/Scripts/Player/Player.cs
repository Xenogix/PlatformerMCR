using UnityEngine;

/// <summary>
/// The command receiver. Commands talk to this façade (Move/Jump/Use) rather than to
/// <see cref="PlayerController"/> directly, which keeps commands decoupled from the
/// controller's internals and lets the same commands drive either the live player or
/// a clone.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class Player : Entity
{
    public PlayerController Controller { get; private set; }

    // The lever/button currently in reach, registered by the usable's trigger.
    private IUsable currentUsable;

    protected virtual void Awake()
    {
        Controller = GetComponent<PlayerController>();
    }

    // --- Command-facing API ---------------------------------------------------

    public void Move(Vector2 direction) => Controller.SetDirection(direction);

    public void Jump() => Controller.RequestJump();

    public void SetJumpHeld(bool held) => Controller.SetJumpHeld(held);

    public void Use() => currentUsable?.Use(this);

    // --- Usable tracking (called by IUsable triggers, e.g. Lever) -------------

    public void SetUsable(IUsable usable) => currentUsable = usable;

    public void ClearUsable(IUsable usable)
    {
        if (ReferenceEquals(currentUsable, usable)) currentUsable = null;
    }
}
