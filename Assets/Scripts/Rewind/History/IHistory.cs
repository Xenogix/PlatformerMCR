// Common storage contract the channel layer depends on. Implemented by both
// DenseHistory<T> (per-tick) and SparseHistory<T> (change-only, carry-forward).
public interface IHistory<T> where T : struct
{
    void Record(int tick, T value);
    T ValueAt(int tick);
    void DiscardAfter(int tick);
    void TrimBefore(int windowStartTick);
}
