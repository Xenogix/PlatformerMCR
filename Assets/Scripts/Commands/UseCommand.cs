using UnityEngine;

/// <summary>
/// Activates whatever the player is currently standing next to (a lever, a button).
/// Emitted only on the tick the "Use" button was pressed.
///
/// Note it carries NO reference to the target lever: <see cref="Player.Use"/> resolves
/// the usable from the player's *own* position. That is why a replaying clone flips
/// whatever lever its replayed position puts it next to — for free.
/// </summary>
public class UseCommand : ICommand
{
public void Execute(Player target)
{
    Debug.Log("UseCommand executed");
    var detector = target.GetComponent<InteractionDetector>();
    Debug.Log("Detector: " + detector);
    var interactable = detector?.GetClosest();
    Debug.Log("Closest interactable: " + interactable);
    interactable?.Interact();
}
    
}
