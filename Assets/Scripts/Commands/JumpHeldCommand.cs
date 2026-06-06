/// <summary>
/// Reports whether the jump button is currently held. Emitted every tick because the
/// held state drives variable jump height (releasing early gives a shorter jump). A
/// clone must receive the same held state each tick or its jump arc would differ from
/// past-you's.
/// </summary>
public class JumpHeldCommand : IStickyCommand
{
    private readonly bool held;

    public JumpHeldCommand(bool held) => this.held = held;

    public void Execute(Player target) => target.SetJumpHeld(held);
}
