using UnityEngine;

/// <summary>
/// The consumer side of the recording: replays a <see cref="CommandTimeline"/> onto a
/// clone. Put this on the clone prefab INSTEAD of <see cref="PlayerCommandInvoker"/>
/// (the clone must not read live input). The clone needs its own
/// <see cref="CharacterController"/>/collider so the live player can stand on it.
///
/// Spawning the clone and handing it a timeline is your teammate's rewind task; call
/// <see cref="Play"/> right after positioning the clone at the recording's start point.
/// </summary>
[RequireComponent(typeof(Player))]
public class ClonePlayback : MonoBehaviour, ITickable
{
    private Player player;
    private PlayerController controller;

    private CommandTimeline timeline;
    private int index;

    /// <summary>True once every recorded tick has been replayed.</summary>
    public bool Finished => timeline == null || index >= timeline.Count;

    private void Awake()
    {
        player = GetComponent<Player>();
        controller = GetComponent<PlayerController>();
    }

    /// <summary>Start replaying the given recording from its first tick.</summary>
    public void Play(CommandTimeline recording)
    {
        timeline = recording;
        index = 0;
    }

    private void OnEnable() => GameClock.Instance.Register(this);

    private void OnDisable()
    {
        if (GameClock.Instance != null) GameClock.Instance.Unregister(this);
    }

    public void Tick(int tick, float dt)
    {
        // Replay by our own index (replay tick 0 = recording index 0), not by the
        // absolute clock tick, so the clone reproduces past-you from its spawn moment.
        if (!Finished)
        {
            TickRecord record = timeline.Get(index);
            foreach (ICommand cmd in record.Commands)
                cmd.Execute(player);
            index++;
        }

        // Keep stepping physics even after the recording ends, so the clone settles
        // naturally (gravity) instead of freezing mid-air.
        controller.Tick(tick, dt);
    }
}
