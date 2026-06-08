using UnityEngine;

[RequireComponent(typeof(PlayerController))]
public class Player : Entity
{
    private PlayerController controller;
    private InteractionDetector interactor;

    private void Awake()
    {
        controller = GetComponent<PlayerController>();
        interactor = GetComponent<InteractionDetector>();
    }

    // Command-receiver facade: bene's retargetable commands call these on whichever
    // Player they're handed — the live one while playing, a clone during replay.
    public void Move(Vector2 direction) => controller.SetDirection(direction);
    public void Jump() => controller.RequestJump();
    public void SetJumpHeld(bool held) => controller.SetJumpHeld(held);

    // Activate the nearest interactable to THIS body's position (a lever/button). UseCommand calls
    // this, so a replaying clone flips whatever its replayed position puts it next to — for free.
    public void Use() => interactor?.GetClosest()?.Interact();
}
