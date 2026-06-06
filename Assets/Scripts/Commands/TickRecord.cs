using System.Collections.Generic;

/// <summary>
/// Everything the player did on a single tick: the tick index plus the commands
/// issued that tick (always at least a Move + JumpHeld, plus Jump/Use when pressed).
/// </summary>
public class TickRecord
{
    public int Tick;
    public List<ICommand> Commands = new List<ICommand>();
}
