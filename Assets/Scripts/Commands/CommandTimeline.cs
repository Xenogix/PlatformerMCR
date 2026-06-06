using System;
using System.Collections.Generic;

/// <summary>
/// A recording of one play-through, stored SPARSELY: a TickRecord is appended only on ticks
/// where input CHANGED (a new movement direction / jump-held edge, or a discrete jump/use
/// press). Continuous state is carried forward in between, so most ticks store nothing — this
/// is what keeps per-tick allocations near zero while still using the Command pattern.
///
/// The recording's extent (the present) is tracked separately by recordingEndTick, advanced
/// every tick, so a clone knows when it has caught up regardless of when the last change was.
/// Frames are addressed by absolute TickRecord.Tick (binary search), never by list position.
///
/// On rewind the live timeline is split at target tick T: SliceFromTick hands a clone a frozen
/// [T, end] copy — WITH the carried-forward sticky state (movement / jump-held begun before T)
/// re-established at T so the clone resumes mid-stride — while TruncateAfterTick drops the live
/// player's post-T records.
/// </summary>
public class CommandTimeline
{
    private readonly List<TickRecord> frames = new List<TickRecord>(); // change ticks only
    private int recordingEndTick = int.MinValue;                       // last recorded tick = the present

    public int LastTick => recordingEndTick; // recording extent (for retire), not the last change

    /// <summary>Advance the recording to `tick`, appending a record only if something changed.</summary>
    public void Record(int tick, List<ICommand> changed)
    {
        recordingEndTick = tick;
        if (changed != null && changed.Count > 0)
            frames.Add(new TickRecord { Tick = tick, Commands = changed });
    }

    public void Clear() { frames.Clear(); recordingEndTick = int.MinValue; }

    // exact index of the frame whose Tick == tick, or -1 (frames are sorted ascending by Tick)
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

    // index of the last frame whose Tick <= tick, or -1
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

    /// <summary>The change-record at absolute `tick`, or null if nothing changed that tick.</summary>
    public TickRecord GetAtTick(int tick)
    {
        int i = IndexOfTick(tick);
        return i < 0 ? null : frames[i];
    }

    /// <summary>Frozen [fromTick, end] copy for a clone, with the carried-forward sticky state
    /// re-established at fromTick so the clone resumes mid-stride.</summary>
    public CommandTimeline SliceFromTick(int fromTick)
    {
        var copy = new CommandTimeline();
        copy.recordingEndTick = recordingEndTick;

        // Latest sticky command of each type carried INTO fromTick (from records before it).
        var openers = new Dictionary<Type, ICommand>();
        int firstIdx = frames.Count;
        for (int i = 0; i < frames.Count; i++)
        {
            if (frames[i].Tick >= fromTick) { firstIdx = i; break; }
            foreach (var c in frames[i].Commands)
                if (c is IStickyCommand) openers[c.GetType()] = c;
        }

        for (int i = firstIdx; i < frames.Count; i++) copy.frames.Add(frames[i]);

        if (openers.Count > 0)
        {
            if (copy.frames.Count > 0 && copy.frames[0].Tick == fromTick)
            {
                // A change already lands on fromTick — prepend only the sticky kinds it lacks.
                var present = new HashSet<Type>();
                foreach (var c in copy.frames[0].Commands) present.Add(c.GetType());
                var merged = new List<ICommand>();
                foreach (var kv in openers) if (!present.Contains(kv.Key)) merged.Add(kv.Value);
                merged.AddRange(copy.frames[0].Commands);
                copy.frames[0] = new TickRecord { Tick = fromTick, Commands = merged };
            }
            else
            {
                copy.frames.Insert(0, new TickRecord { Tick = fromTick, Commands = new List<ICommand>(openers.Values) });
            }
        }
        return copy;
    }

    /// <summary>Keep change-records with Tick &lt;= tick; set the recording end to tick so the
    /// live player re-records forward from tick+1.</summary>
    public void TruncateAfterTick(int tick)
    {
        int keep = FloorIndexOfTick(tick) + 1; // -1 -> 0
        if (keep < frames.Count) frames.RemoveRange(keep, frames.Count - keep);
        recordingEndTick = tick;
    }
}
