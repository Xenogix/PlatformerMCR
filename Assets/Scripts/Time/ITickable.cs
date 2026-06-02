/// <summary>
/// Implemented by anything that must advance on the deterministic, fixed-step
/// timeline driven by <see cref="GameClock"/>. The same <paramref name="tick"/>
/// index is replayed onto clones so recorded commands reproduce exactly.
/// </summary>
public interface ITickable
{
    void Tick(int tick, float dt);
}
