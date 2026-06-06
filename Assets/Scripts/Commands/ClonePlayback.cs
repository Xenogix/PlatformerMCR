using UnityEngine;

/// <summary>
/// The consumer side of the recording: replays a <see cref="CommandTimeline"/> onto a
/// clone (echo). Put this on the echo INSTEAD of <see cref="PlayerCommandInvoker"/> (the
/// echo must not read live input), alongside a RewindableEntity + RigidbodyChannel so the
/// echo snaps to its exact recorded pose when the world is rewound again.
///
/// Replay is addressed by ABSOLUTE tick (GetAtTick), not a spawn-relative index: the echo
/// re-executes the command recorded for the current clock tick, so a clock rewind
/// automatically rewinds the replay and its pose is restored by its RewindableEntity.
/// Forward, physics is non-deterministic so the echo drifts from past-you — that
/// divergence is the point (stack several and it gets chaotic).
/// </summary>
[RequireComponent(typeof(Player))]
public class ClonePlayback : MonoBehaviour, ITickable
{
    private Player player;
    private PlayerController controller;
    private CommandTimeline timeline;

    private void Awake()
    {
        player = GetComponent<Player>();
        controller = GetComponent<PlayerController>();
    }

    /// <summary>Begin replaying the given recording (its frames carry absolute ticks).</summary>
    public void Play(CommandTimeline recording) => timeline = recording;

    private void OnEnable() => GameClock.Instance.Register(this);

    private void OnDisable()
    {
        if (GameClock.HasInstance) GameClock.Instance.Unregister(this);
    }

    public void Tick(int tick, float dt)
    {
        // Past the end of the recording: the echo has replayed its whole [T, present] window. It
        // does NOT loop and is NOT retired — it just stops being driven and lives on as an ordinary
        // physics body (gravity, friction, collisions, just like the player with no input). It stays
        // registered + captured, so a rewind back into its window snaps it and resumes the replay.
        if (timeline == null || tick > timeline.LastTick) return;

        TickRecord record = timeline.GetAtTick(tick);
        if (record != null)
            foreach (ICommand cmd in record.Commands) cmd.Execute(player);
        // else: nothing changed this tick — carry forward the controller's current state
        // (sparse recording; the slice re-established the sticky state at its start).

        controller.Tick(tick, dt); // advance physics each tick so the echo moves as recorded
    }
}
