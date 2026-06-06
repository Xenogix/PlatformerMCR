/// <summary>
/// Requests a jump. Emitted only on the tick the jump button was pressed.
/// </summary>
public class JumpCommand : ICommand
{
    public void Execute(Player target) => target.Jump();
}
