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
        if (timeline == null) return;

        // Past the end of the recording: the echo has replayed its whole [T, present] window. It
        // does NOT loop and is NOT retired — instead it keeps being driven with NEUTRAL input, so
        // the controller decelerates it to a stop and keeps applying gravity, exactly like the
        // player standing still with no keys pressed. (The collider is frictionless by design — the
        // controller, not the physics material, handles stopping; without this the echo would keep
        // its velocity and slide forever.) It stays registered + captured, so a rewind back into its
        // window snaps it and resumes the recorded replay.
        if (tick > timeline.LastTick)
        {
            player.Move(Vector2.zero);
            player.SetJumpHeld(false);
            controller.Tick(tick, dt);
            return;
        }

        TickRecord record = timeline.GetAtTick(tick);
        if (record != null)
            foreach (ICommand cmd in record.Commands) cmd.Execute(player);
        // else: nothing changed this tick — carry forward the controller's current state
        // (sparse recording; the slice re-established the sticky state at its start).

        controller.Tick(tick, dt); // advance physics each tick so the echo moves as recorded
    }
}
