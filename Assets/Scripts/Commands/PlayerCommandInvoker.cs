using System;
using System.Collections.Generic;
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

    // Change-detection for sparse recording: emit a Move/JumpHeld command only when it differs
    // from the last tick (continuous state is carried forward). hasRecorded forces the first
    // tick to emit both, establishing the initial sticky state.
    private Vector2 lastMove;
    private bool lastJumpHeld;
    private bool hasRecorded;

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
        Vector2 move = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
        bool jumpHeld = jumpAction != null && jumpAction.IsPressed();

        // Record (and execute) ONLY what changed this tick; continuous state carries forward.
        List<ICommand> changed = null;
        if (!hasRecorded || move != lastMove)
            (changed ??= new List<ICommand>()).Add(new MoveCommand(move));
        if (!hasRecorded || jumpHeld != lastJumpHeld)
            (changed ??= new List<ICommand>()).Add(new JumpHeldCommand(jumpHeld));
        if (jumpPressedThisTick) (changed ??= new List<ICommand>()).Add(new JumpCommand());
        if (usePressedThisTick) (changed ??= new List<ICommand>()).Add(new UseCommand());

        lastMove = move;
        lastJumpHeld = jumpHeld;
        hasRecorded = true;

        // 1) drive the live player with the changes, 2) advance physics, 3) record.
        if (changed != null)
            foreach (ICommand cmd in changed) cmd.Execute(player);
        controller.Tick(tick, dt);
        Timeline.Record(tick, changed);

        jumpPressedThisTick = false;
        usePressedThisTick = false;
    }
}
