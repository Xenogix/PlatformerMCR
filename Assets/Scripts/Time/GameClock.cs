using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Single source of "game time", advancing one fixed tick per FixedUpdate. Each tick it runs
/// two groups with the same integer tick index, then increments it:
///   1. MOVERS (player invoker, clone playbacks) — set velocities for this tick.
///   2. OBSERVERS (the rewind caretaker) — run AFTER the movers, so they capture this tick's
///      post-move state.
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

    /// <summary>Convert a duration in seconds to a count of fixed ticks (min 1) — the single
    /// home for the seconds→ticks rule used by jump buffers, rewind offsets, and windows.</summary>
    public static int SecondsToTicks(float seconds) => Mathf.Max(1, Mathf.RoundToInt(seconds / Time.fixedDeltaTime));

    private readonly TickGroup _movers = new TickGroup();
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
    public void RegisterPost(ITickable tickable) => _observers.Register(tickable);
    public void UnregisterPost(ITickable tickable) => _observers.Unregister(tickable);

    /// <summary>
    /// Wind the clock back to an earlier tick. The rewind feature calls this so clock-relative
    /// consumers (clone playback) reset their replay position for free; per-tick world state is
    /// restored separately by the rewind caretaker.
    /// </summary>
    public void RewindTo(int tick) => Tick = Mathf.Max(0, tick);

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        _movers.Tick(Tick, dt);
        _observers.Tick(Tick, dt);
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
            for (int i = 0; i < _live.Count; i++) _live[i].Tick(tick, dt);
        }
    }
}
