using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Single source of "game time", advancing in fixed steps so that gameplay is
/// deterministic: re-running the same commands on the same ticks reproduces the
/// same physics. This is what lets a rewound clone land exactly where past-you did.
///
/// Every <see cref="ITickable"/> (the player's invoker, each clone's playback)
/// registers here and is ticked once per <see cref="FixedUpdate"/>, in registration
/// order, with the shared integer tick index.
///
/// The instance is created lazily, so you don't strictly need to place a GameClock
/// object in the scene — but you can, if you want to control where it lives.
/// </summary>
public class GameClock : MonoBehaviour
{
    private static GameClock instance;

    public static GameClock Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindFirstObjectByType<GameClock>();
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

    // Buffered so a tickable can (un)register during a tick without mutating the
    // list we're iterating (e.g. a clone that registers itself the moment it spawns).
    private readonly List<ITickable> pendingAdd = new List<ITickable>();
    private readonly List<ITickable> pendingRemove = new List<ITickable>();

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

    public void Register(ITickable tickable)
    {
        if (!tickables.Contains(tickable) && !pendingAdd.Contains(tickable))
            pendingAdd.Add(tickable);
    }

    public void Unregister(ITickable tickable)
    {
        if (!pendingRemove.Contains(tickable))
            pendingRemove.Add(tickable);
    }

    private void FixedUpdate()
    {
        FlushPending();

        int current = Tick;
        for (int i = 0; i < tickables.Count; i++)
            tickables[i].Tick(current, Time.fixedDeltaTime);

        Tick++;
    }

    private void FlushPending()
    {
        if (pendingRemove.Count > 0)
        {
            foreach (var t in pendingRemove) tickables.Remove(t);
            pendingRemove.Clear();
        }
        if (pendingAdd.Count > 0)
        {
            tickables.AddRange(pendingAdd);
            pendingAdd.Clear();
        }
    }
}
