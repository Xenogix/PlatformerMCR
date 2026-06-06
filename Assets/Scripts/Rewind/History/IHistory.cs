// Common storage contract the channel layer depends on. Implemented by both
// DenseHistory<T> (per-tick) and SparseHistory<T> (change-only, carry-forward).
public interface IHistory<T> where T : struct
{
    void Record(int tick, T value);
    // Safe read: false (and value=default) when there is no value at or before `tick`,
    // so a channel restored before its first capture writes nothing instead of throwing.
    bool TryValueAt(int tick, out T value);
    void DiscardAfter(int tick);
    void TrimBefore(int windowStartTick);
}
