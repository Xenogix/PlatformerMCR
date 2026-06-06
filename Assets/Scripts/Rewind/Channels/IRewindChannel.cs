// Non-generic face the RewindableEntity drives each tick. A RewindChannel<T>
// implements this and owns its own history; the entity just holds IRewindChannel[].
public interface IRewindChannel
{
    void Capture(int tick);
    void Restore(int tick);
    void DiscardAfter(int tick);
    void TrimBefore(int windowStartTick);
}
