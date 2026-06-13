using UnityEngine;

// A rewindable slice of an object. It reads/writes its own state (Read/Write) and
// owns the history that records it over time. The storage *logic* still lives in the
// pure, reusable IHistory<T> it creates via NewHistory() (dense vs sparse) — the
// channel just owns an instance and delegates the tick operations to it.
//
// Unity attaches the concrete non-generic subclass (e.g. RigidbodyChannel); this
// abstract generic base is never added directly.
public abstract class RewindChannel<T> : MonoBehaviour, IRewindChannel where T : struct
{
    private IHistory<T> _history;

    // The owned history, exposed to subclasses that need to export the recorded run
    // (e.g. RigidbodyChannel turning a dense capture into a best-run shadow path).
    protected IHistory<T> History => _history;

    protected abstract T Read();                  // live -> memento
    protected abstract void Write(T state);       // memento -> live
    protected abstract IHistory<T> NewHistory();  // storage strategy (dense/sparse)

    protected virtual void Awake() => _history = NewHistory();

    public void Capture(int tick) => _history.Record(tick, Read());
    public void Restore(int tick) { if (_history.TryValueAt(tick, out var state)) Write(state); }
    public void DiscardAfter(int tick) => _history.DiscardAfter(tick);
    public void TrimBefore(int windowStartTick) => _history.TrimBefore(windowStartTick);
    public void Clear() => _history.Clear();
}
