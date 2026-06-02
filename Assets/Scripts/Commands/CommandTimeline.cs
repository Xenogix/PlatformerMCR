using System.Collections.Generic;

/// <summary>
/// An ordered, dense recording of one play-through: one <see cref="TickRecord"/> per
/// tick. The invoker appends as the player plays; <see cref="ClonePlayback"/> reads it
/// back index-by-index (its own replay tick 0 = recording index 0), so there are no
/// gaps to reason about.
///
/// This is the data your teammate's rewind feature hands to a freshly spawned clone.
/// </summary>
public class CommandTimeline
{
    private readonly List<TickRecord> frames = new List<TickRecord>();

    public int Count => frames.Count;

    public void Append(TickRecord frame) => frames.Add(frame);

    public TickRecord Get(int index) => frames[index];

    public void Clear() => frames.Clear();
}
