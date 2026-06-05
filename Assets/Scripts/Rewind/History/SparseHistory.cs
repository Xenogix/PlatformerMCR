using System;
using System.Collections.Generic;

// Sparse change-only history: records a value only when it differs from the last,
// carrying it forward for later queries. Entries are sorted by tick, so all lookups
// use binary search (O(log n)).
public sealed class SparseHistory<T> : IHistory<T> where T : struct, IEquatable<T>
{
    private readonly List<int> _ticks = new();
    private readonly List<T> _values = new();

    public int Count => _ticks.Count;

    // Tick of the most recent entry, or int.MinValue if empty. Used by reclamation:
    // a dormant entity whose last alive-change is at/before the window start can
    // never be revived again, so it can be destroyed.
    public int LastTick => _ticks.Count == 0 ? int.MinValue : _ticks[_ticks.Count - 1];

    // Index of the last entry whose tick <= `tick`, or -1 if none. Binary search.
    private int FloorIndex(int tick)
    {
        int lo = 0, hi = _ticks.Count - 1, res = -1;
        while (lo <= hi)
        {
            int mid = (int)(((uint)lo + (uint)hi) >> 1);
            if (_ticks[mid] <= tick) { res = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return res;
    }

    public void Record(int tick, T value)
    {
        if (_values.Count > 0 && value.Equals(_values[_values.Count - 1])) return; // change-suppression
        _ticks.Add(tick);
        _values.Add(value);
    }

    public T ValueAt(int tick) => _values[FloorIndex(tick)];

    // Returns `fallback` when no entry exists at or before `tick`.
    public T ValueAtOr(int tick, T fallback)
    {
        int idx = FloorIndex(tick);
        return idx < 0 ? fallback : _values[idx];
    }

    public void DiscardAfter(int tick)
    {
        int keep = FloorIndex(tick) + 1; // -1 -> keep 0
        _ticks.RemoveRange(keep, _ticks.Count - keep);
        _values.RemoveRange(keep, _values.Count - keep);
    }

    // Keep the last entry at or before windowStart (the anchor that carries the
    // governing value into the window) plus everything after it.
    public void TrimBefore(int windowStartTick)
    {
        int anchor = FloorIndex(windowStartTick);
        if (anchor > 0)
        {
            _ticks.RemoveRange(0, anchor);
            _values.RemoveRange(0, anchor);
        }
    }
}
