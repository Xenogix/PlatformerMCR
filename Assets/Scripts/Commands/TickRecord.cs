using System.Collections.Generic;

/// <summary>
/// The commands recorded for a single tick where input CHANGED (a new movement direction or
/// jump-held edge, or a discrete jump/use press). Ticks with no change store no record at all
/// — the continuous state is carried forward. Commands is always assigned by CommandTimeline.
/// </summary>
public class TickRecord
{
    public int Tick;
    public List<ICommand> Commands;
}
