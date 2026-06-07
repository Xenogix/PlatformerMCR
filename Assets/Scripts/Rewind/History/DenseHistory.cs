using System;
using System.Collections.Generic;

// Dense per-tick history for REGULAR-cadence capture (constant tick spacing, as the
// RewindCaretaker produces). Stores ONLY values; the tick of entry i is
// _baseTick + i*_step, so lookup is O(1) index arithmetic and no per-entry tick
// is stored. Out-of-range queries clamp to the earliest/latest entry.
public sealed class DenseHistory<T> : IHistory<T> where T : struct
{
    private readonly List<T> _values = new();
    private int _baseTick;   // tick of _values[0]
    private int _step;       // constant tick spacing (inferred from the first two records; 0 while <2 entries)

    public void Record(int tick, T value)
    {
        if (_values.Count == 0) { _baseTick = tick; _step = 0; }
        else if (_step == 0) { _step = tick - _baseTick; }
        _values.Add(value); // contract: tick == _baseTick + _values.Count*_step
    }

    public bool TryValueAt(int tick, out T value)
    {
        if (_values.Count == 0) { value = default; return false; }
        int idx = _step <= 0 ? 0 : (tick - _baseTick) / _step;
        if (idx < 0) idx = 0;                               // before earliest -> clamp
        else if (idx >= _values.Count) idx = _values.Count - 1; // after latest -> last
        value = _values[idx];
        return true;
    }

    // Drop everything, resetting to empty so the next Record starts a fresh dense run.
    public void Clear()
    {
        _values.Clear();
        _baseTick = 0;
        _step = 0;
    }

    // Rewind: drop every entry recorded strictly after `tick`.
    public void DiscardAfter(int tick)
    {
        if (_values.Count == 0) return;
        int keep;
        if (_step <= 0) keep = tick >= _baseTick ? _values.Count : 0;
        else
        {
            int idx = (tick - _baseTick) / _step;           // index of nearest entry <= tick
            keep = idx < 0 ? 0 : Math.Min(idx + 1, _values.Count);
        }
        _values.RemoveRange(keep, _values.Count - keep);
    }

    // Window eviction: drop entries with tick < windowStart (a prefix). Dense capture
    // lands an entry on every cadence tick, so the kept head still starts the window.
    public void TrimBefore(int windowStartTick)
    {
        if (_values.Count == 0 || _step <= 0) return;
        if (windowStartTick <= _baseTick) return;
        int drop = (windowStartTick - 1 - _baseTick) / _step + 1; // count of entries with tick < windowStart
        if (drop > _values.Count) drop = _values.Count;
        _values.RemoveRange(0, drop);
        _baseTick += drop * _step;
    }
}
