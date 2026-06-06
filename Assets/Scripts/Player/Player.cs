using UnityEngine;

[RequireComponent(typeof(PlayerController))]
public class Player : Entity
{
    private PlayerController controller;

    private void Awake() => controller = GetComponent<PlayerController>();

    // Command-receiver facade: bene's retargetable commands call these on whichever
    // Player they're handed — the live one while playing, a clone during replay.
    public void Move(Vector2 direction) => controller.SetDirection(direction);
    public void Jump() => controller.RequestJump();
    public void SetJumpHeld(bool held) => controller.SetJumpHeld(held);

    // TODO: usable/lever interaction (deferred with the StateChannel work). UseCommand
    // resolves the target from the player's OWN position, so a replaying clone will
    // flip whatever it's standing next to for free once this is implemented.
    public void Use() { }
}
