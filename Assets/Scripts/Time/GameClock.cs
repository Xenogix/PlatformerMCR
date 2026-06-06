using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Single source of "game time", advancing in fixed steps so that gameplay is
/// deterministic-ish: re-running the same commands on the same ticks reproduces (close
/// to) the same physics. This is what lets a rewound clone retrace what past-you did.
///
/// Two phases per <see cref="FixedUpdate"/>, both with the same integer tick index:
///   1. <see cref="ITickable"/>s registered via <see cref="Register"/> — the MOVERS
///      (player invoker, clone playbacks) that set velocities for this tick.
///   2. post-tickables registered via <see cref="RegisterPost"/> — OBSERVERS (the rewind
///      caretaker) that run AFTER the movers, so they capture this tick's post-move state.
///
/// The instance is created lazily, so you don't strictly need to place a GameClock
/// object in the scene — but you can, if you want to control where it lives.
/// </summary>
public class GameClock : MonoBehaviour
{
    private static GameClock instance;

    /// <summary>True if an instance exists WITHOUT creating one — safe to use in
    /// OnDisable/OnDestroy/teardown, where the auto-creating Instance getter would otherwise
    /// resurrect a clock.</summary>
    public static bool HasInstance => instance != null;

    public static GameClock Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindAnyObjectByType<GameClock>();
                if (instance == null)
                {
                    var go = new GameObject(nameof(GameClock));
                    instance = go.AddComponent<GameClock>();
                }
            }
            return instance;
        }
    }

    /// <summary>The current fixed-step tick index. Starts at 0, increments after each step.</summary>
    public int Tick { get; private set; }

    private readonly List<ITickable> tickables = new List<ITickable>();
    private readonly List<ITickable> postTickables = new List<ITickable>();

    // Buffered so a tickable can (un)register during a tick without mutating a list
    // we're iterating (e.g. a clone that registers the moment it spawns, or a copied
    // invoker that is stripped off an echo the same frame it was instantiated).
    private readonly List<ITickable> pendingAdd = new List<ITickable>();
    private readonly List<ITickable> pendingRemove = new List<ITickable>();
    private readonly List<ITickable> pendingPostAdd = new List<ITickable>();
    private readonly List<ITickable> pendingPostRemove = new List<ITickable>();

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    public void Register(ITickable tickable) => Enqueue(tickable, pendingAdd, pendingRemove, tickables);
    public void Unregister(ITickable tickable) => Dequeue(tickable, pendingAdd, pendingRemove);
    public void RegisterPost(ITickable tickable) => Enqueue(tickable, pendingPostAdd, pendingPostRemove, postTickables);
    public void UnregisterPost(ITickable tickable) => Dequeue(tickable, pendingPostAdd, pendingPostRemove);

    // Register/unregister cancel each other within a single tick window: if a tickable
    // is added then removed before the next flush (spawn + strip in one frame), it ends
    // up NOT registered, instead of being left in the list as a destroyed component.
    private static void Enqueue(ITickable t, List<ITickable> add, List<ITickable> remove, List<ITickable> live)
    {
        remove.Remove(t);
        if (!live.Contains(t) && !add.Contains(t)) add.Add(t);
    }

    private static void Dequeue(ITickable t, List<ITickable> add, List<ITickable> remove)
    {
        if (add.Remove(t)) return; // never really added — cancel it
        if (!remove.Contains(t)) remove.Add(t);
    }

    /// <summary>
    /// Wind the clock back to an earlier tick. The rewind feature calls this so that
    /// clock-relative consumers (clone playback) reset their replay position for free;
    /// the per-tick world state is restored separately by the rewind caretaker.
    /// </summary>
    public void RewindTo(int tick) => Tick = Mathf.Max(0, tick);

    private void FixedUpdate()
    {
        int current = Tick;
        float dt = Time.fixedDeltaTime;

        Flush(tickables, pendingAdd, pendingRemove);
        for (int i = 0; i < tickables.Count; i++) tickables[i].Tick(current, dt);

        Flush(postTickables, pendingPostAdd, pendingPostRemove);
        for (int i = 0; i < postTickables.Count; i++) postTickables[i].Tick(current, dt);

        Tick++;
    }

    private static void Flush(List<ITickable> live, List<ITickable> add, List<ITickable> remove)
    {
        if (remove.Count > 0)
        {
            foreach (var t in remove) live.Remove(t);
            remove.Clear();
        }
        if (add.Count > 0)
        {
            live.AddRange(add);
            add.Clear();
        }
    }
}
