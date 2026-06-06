using UnityEngine;

/// <summary>
/// Sets the player's movement direction for this tick. Emitted every tick (the
/// direction may be <see cref="Vector2.zero"/>) so replay reproduces movement exactly.
/// </summary>
public class MoveCommand : IStickyCommand
{
    private readonly Vector2 direction;

    public MoveCommand(Vector2 direction) => this.direction = direction;

    public void Execute(Player target) => target.Move(direction);
}
