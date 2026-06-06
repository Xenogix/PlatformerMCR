/// <summary>
/// Reports whether the jump button is currently held — this drives variable jump height
/// (releasing early gives a shorter jump). A sticky command: emitted only when the held
/// state CHANGES (press/release) and carried forward in between, so a replaying clone's
/// jump arc matches past-you's without needing a command every tick.
/// </summary>
public class JumpHeldCommand : IStickyCommand
{
    private readonly bool held;

    public JumpHeldCommand(bool held) => this.held = held;

    public void Execute(Player target) => target.SetJumpHeld(held);
}
