using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The Command pattern's invoker. Each fixed tick it turns the current input into
/// command objects, executes them on the live <see cref="Player"/>, and appends them
/// to <see cref="Timeline"/> so a clone can replay the exact same commands later.
///
/// Replaces the old PlayerControllerInput (which called the controller directly).
/// Lives on the Player GameObject alongside <see cref="Player"/> and
/// <see cref="PlayerController"/>.
/// </summary>
[RequireComponent(typeof(Player))]
public class PlayerCommandInvoker : MonoBehaviour, ITickable
{
    private Player player;
    private PlayerController controller;

    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction useAction;

    // Discrete presses are latched in the Update-driven input callbacks and consumed
    // on the next fixed tick, so a press between two ticks is counted exactly once.
    private bool jumpPressedThisTick;
    private bool usePressedThisTick;

    private Action<InputAction.CallbackContext> onJumpStarted;
    private Action<InputAction.CallbackContext> onUseStarted;

    /// <summary>The recording of everything done so far — handed to a clone on rewind.</summary>
    public CommandTimeline Timeline { get; } = new CommandTimeline();

    private void Awake()
    {
        player = GetComponent<Player>();
        controller = GetComponent<PlayerController>();

        moveAction = InputSystem.actions.FindAction("Move");
        jumpAction = InputSystem.actions.FindAction("Jump");
        useAction = InputSystem.actions.FindAction("Use"); // may be null until the action is added
        if (moveAction == null) Debug.LogError("PlayerCommandInvoker: 'Move' input action not found.");
        if (jumpAction == null) Debug.LogError("PlayerCommandInvoker: 'Jump' input action not found.");

        onJumpStarted = _ => jumpPressedThisTick = true;
        onUseStarted = _ => usePressedThisTick = true;
    }

    private void OnEnable()
    {
        if (jumpAction != null) jumpAction.started += onJumpStarted;
        if (useAction != null) useAction.started += onUseStarted;
        GameClock.Instance.Register(this);
    }

    private void OnDisable()
    {
        if (jumpAction != null) jumpAction.started -= onJumpStarted;
        if (useAction != null) useAction.started -= onUseStarted;
        if (GameClock.HasInstance) GameClock.Instance.Unregister(this);
    }

    public void Tick(int tick, float dt)
    {
        var record = new TickRecord { Tick = tick };

        // Continuous state, recorded every tick:
        record.Commands.Add(new MoveCommand(moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero));
        record.Commands.Add(new JumpHeldCommand(jumpAction != null && jumpAction.IsPressed()));

        // Discrete presses, recorded only on the tick they happened:
        if (jumpPressedThisTick) record.Commands.Add(new JumpCommand());
        if (usePressedThisTick) record.Commands.Add(new UseCommand());

        // 1) drive the live player, 2) advance physics, 3) save for replay.
        foreach (ICommand cmd in record.Commands)
            cmd.Execute(player);

        controller.Tick(tick, dt);

        Timeline.Append(record);

        jumpPressedThisTick = false;
        usePressedThisTick = false;
    }
}
