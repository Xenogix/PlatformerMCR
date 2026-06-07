using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Single source of "game time", advancing one fixed tick per FixedUpdate. Each tick it runs
/// two groups with the same integer tick index, then increments it:
///   1. OBSERVERS (the rewind caretaker) — run FIRST, snapshotting each tick's ENTERING state:
///      the position and velocity the movers are about to act on. This pairing is consistent
///      (the body hasn't been advanced by this tick yet), so restoring it and re-running the
///      tick reproduces the tick exactly. Capturing AFTER the movers stored a velocity already
///      advanced by this tick's gravity/acceleration, which a rewind would then advance a
///      SECOND time (an extra tick of gravity per commit) — that is the bug this ordering fixes.
///      Reviving a dormant entity here (PrepareCapture) also registers it into the mover group
///      in time to tick THIS frame, so a clone replayed back into its window resumes on its
///      spawn tick rather than one tick late.
///   2. MOVERS (player invoker, clone playbacks) — set velocities for this tick.
/// Created lazily, so a GameClock object need not be placed in the scene.
/// </summary>
public class GameClock : MonoBehaviour
{
    private static GameClock instance;

    /// <summary>True if an instance exists WITHOUT creating one — safe in teardown/OnDisable.</summary>
    public static bool HasInstance => instance != null;

    public static GameClock Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindAnyObjectByType<GameClock>();
                if (instance == null)
                    instance = new GameObject(nameof(GameClock)).AddComponent<GameClock>();
            }
            return instance;
        }
    }

    /// <summary>The current fixed tick index. Starts at 0, increments after each tick.</summary>
    public int Tick { get; private set; }

    /// <summary>True while game time is frozen (e.g. the timeline is open for scrubbing).</summary>
    public bool IsPaused { get; private set; }

    /// <summary>
    /// Freeze/unfreeze game time. Sets <see cref="Time.timeScale"/> to 0/1 so Unity's own
    /// physics auto-simulation and FixedUpdate stop too (otherwise rigidbodies keep falling
    /// while we scrub). UI animations use unscaled time, so they keep running while paused.
    /// </summary>
    public void SetPaused(bool paused)
    {
        IsPaused = paused;
        Time.timeScale = paused ? 0f : 1f;
    }

    /// <summary>Convert a duration in seconds to a count of fixed ticks (min 1) — the single
    /// home for the seconds→ticks rule used by jump buffers, rewind offsets, and windows.</summary>
    public static int SecondsToTicks(float seconds) => Mathf.Max(1, Mathf.RoundToInt(seconds / Time.fixedDeltaTime));

    // Movers tick bottom-up (sorted by world Y): a carrier ticks before the rider standing on it, so
    // the rider reads the carrier's FRESH velocity this tick (a jump included) and matches it — instead
    // of the dynamic solver splitting the jump's momentum between the two stacked bodies.
    private readonly TickGroup _movers = new TickGroup(orderByHeight: true);
    private readonly TickGroup _observers = new TickGroup();

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    public void Register(ITickable tickable) => _movers.Register(tickable);
    public void Unregister(ITickable tickable) => _movers.Unregister(tickable);
    // Observers snapshot the world; they run before the movers each tick (see class summary).
    public void RegisterObserver(ITickable tickable) => _observers.Register(tickable);
    public void UnregisterObserver(ITickable tickable) => _observers.Unregister(tickable);

    /// <summary>
    /// Wind the clock back to an earlier tick. The rewind feature calls this so clock-relative
    /// consumers (clone playback) reset their replay position for free; per-tick world state is
    /// restored separately by the rewind caretaker.
    /// </summary>
    public void RewindTo(int tick) => Tick = Mathf.Max(0, tick);

    private void FixedUpdate()
    {
        if (IsPaused) return; // frozen while scrubbing; timeScale 0 already stops this, kept explicit
        float dt = Time.fixedDeltaTime;
        _observers.Tick(Tick, dt); // snapshot the ENTERING state first (see class summary)
        _movers.Tick(Tick, dt);
        Tick++;
    }

    /// <summary>
    /// A set of ITickables ticked together, with buffered (un)registration so a tickable can
    /// (un)register during a tick without disturbing iteration. An add and a remove requested in
    /// the same window cancel out, so a spawn-then-strip (e.g. an echo's copied invoker) never
    /// leaves a destroyed tickable in the live list.
    /// </summary>
    private sealed class TickGroup
    {
        private readonly List<ITickable> _live = new List<ITickable>();
        private readonly List<ITickable> _pendingAdd = new List<ITickable>();
        private readonly List<ITickable> _pendingRemove = new List<ITickable>();
        private readonly bool _orderByHeight;

        public TickGroup(bool orderByHeight = false) => _orderByHeight = orderByHeight;

        public void Register(ITickable t)
        {
            _pendingRemove.Remove(t);
            if (!_live.Contains(t) && !_pendingAdd.Contains(t)) _pendingAdd.Add(t);
        }

        public void Unregister(ITickable t)
        {
            if (_pendingAdd.Remove(t)) return;          // never really added — cancel
            if (!_pendingRemove.Contains(t)) _pendingRemove.Add(t);
        }

        public void Tick(int tick, float dt)
        {
            if (_pendingRemove.Count > 0)
            {
                foreach (var t in _pendingRemove) _live.Remove(t);
                _pendingRemove.Clear();
            }
            if (_pendingAdd.Count > 0)
            {
                _live.AddRange(_pendingAdd);
                _pendingAdd.Clear();
            }
            if (_orderByHeight) SortByHeight();
            for (int i = 0; i < _live.Count; i++) _live[i].Tick(tick, dt);
        }

        private void SortByHeight()
        {
            for (int i = 1; i < _live.Count; i++)
            {
                ITickable item = _live[i];
                float key = HeightOf(item);
                int j = i - 1;
                while (j >= 0 && HeightOf(_live[j]) > key) { _live[j + 1] = _live[j]; j--; }
                _live[j + 1] = item;
            }
        }

        private static float HeightOf(ITickable t) => t is MonoBehaviour mb ? mb.transform.position.y : 0f;
    }
}
