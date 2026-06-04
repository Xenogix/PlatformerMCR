using System;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerController))]
public class PlayerControllerInput : MonoBehaviour
{
    private PlayerController playerController;
    private CommandInvoker invoker;

    private InputAction moveAction;
    private InputAction jumpAction;

    private Action<InputAction.CallbackContext> onJumpStarted;
    private Action<InputAction.CallbackContext> onJumpCanceled;

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
        invoker = new CommandInvoker();

        moveAction = InputSystem.actions.FindAction("Move");
        jumpAction = InputSystem.actions.FindAction("Jump");
if (moveAction == null) Debug.LogError("Move action not found!");
if (jumpAction == null) Debug.LogError("Jump action not found!");
        onJumpStarted  = _ => invoker.Execute(new JumpCommand(playerController));
        onJumpCanceled = _ => playerController.SetJumpHeld(false); 
    }

    private void OnEnable()
    {
        jumpAction.started += onJumpStarted;
        jumpAction.canceled += onJumpCanceled;
    }

    private void OnDisable()
    {
        jumpAction.started -= onJumpStarted;
        jumpAction.canceled -= onJumpCanceled;
    }

    private void Update()
    {
        invoker.Execute(new MoveCommand(playerController, moveAction.ReadValue<Vector2>()));
    }
}