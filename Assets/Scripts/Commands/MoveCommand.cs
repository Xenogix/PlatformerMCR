using UnityEngine;

/// <summary>
/// Sets the player's movement direction. A sticky command: emitted only when the
/// direction CHANGES and carried forward in between, so replay reproduces movement
/// exactly without a command every tick.
/// </summary>
public class MoveCommand : IStickyCommand
{
    private readonly Vector2 direction;

    public MoveCommand(Vector2 direction) => this.direction = direction;

    public void Execute(Player target) => target.Move(direction);
}
