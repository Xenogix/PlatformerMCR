using System.Collections.Generic;

/// <summary>
/// An ordered, dense recording of one play-through: one <see cref="TickRecord"/> per
/// tick, appended in ascending tick order. Addressed by ABSOLUTE tick (via
/// <see cref="TickRecord.Tick"/>, binary search) rather than by list position, so the
/// command side stays correct even if recording starts at a tick &gt; 0 or the head is
/// ever trimmed — it never assumes index == tick.
///
/// On rewind the caretaker resolves a target tick T; the live timeline is split there:
/// <see cref="SliceFromTick"/> hands a frozen [T, now] copy to a freshly spawned clone,
/// and <see cref="TruncateAfterTick"/> drops the live player's post-T frames so it
/// re-records forward. Note the asymmetry vs the caretaker's state DiscardAfter: state
/// after T is discarded, but the commands after T are RETAINED in the clone's slice.
/// </summary>
public class CommandTimeline
{
    private readonly List<TickRecord> frames = new List<TickRecord>();

    public int Count => frames.Count;
    public int FirstTick => frames.Count == 0 ? int.MaxValue : frames[0].Tick;
    public int LastTick => frames.Count == 0 ? int.MinValue : frames[frames.Count - 1].Tick;

    public void Append(TickRecord frame) => frames.Add(frame);
    public void Clear() => frames.Clear();

    // Exact index of the frame whose Tick == tick, or -1. Frames are sorted by Tick.
    private int IndexOfTick(int tick)
    {
        int lo = 0, hi = frames.Count - 1;
        while (lo <= hi)
        {
            int mid = (int)(((uint)lo + (uint)hi) >> 1);
            int t = frames[mid].Tick;
            if (t == tick) return mid;
            if (t < tick) lo = mid + 1; else hi = mid - 1;
        }
        return -1;
    }

    // Index of the last frame whose Tick <= tick, or -1 if none.
    private int FloorIndexOfTick(int tick)
    {
        int lo = 0, hi = frames.Count - 1, res = -1;
        while (lo <= hi)
        {
            int mid = (int)(((uint)lo + (uint)hi) >> 1);
            if (frames[mid].Tick <= tick) { res = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return res;
    }

    /// <summary>The record recorded for absolute <paramref name="tick"/>, or null if none.</summary>
    public TickRecord GetAtTick(int tick)
    {
        int i = IndexOfTick(tick);
        return i < 0 ? null : frames[i];
    }

    /// <summary>A frozen copy of every frame with Tick &gt;= fromTick — handed to a clone on
    /// rewind. Shares the (immutable-once-recorded) TickRecord instances; only the list is copied.</summary>
    public CommandTimeline SliceFromTick(int fromTick)
    {
        var copy = new CommandTimeline();
        for (int i = 0; i < frames.Count; i++)
            if (frames[i].Tick >= fromTick) copy.frames.Add(frames[i]);
        return copy;
    }

    /// <summary>Keep frames with Tick &lt;= tick, drop the rest, so the live player re-records
    /// from tick+1.</summary>
    public void TruncateAfterTick(int tick)
    {
        int keep = FloorIndexOfTick(tick) + 1; // -1 -> keep 0
        if (keep < frames.Count) frames.RemoveRange(keep, frames.Count - keep);
    }
}
