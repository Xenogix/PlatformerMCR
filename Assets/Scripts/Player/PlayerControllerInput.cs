using System;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerController))]
public class PlayerControllerInput : MonoBehaviour
{
    private PlayerController playerController;

    private InputAction moveAction;
    private InputAction jumpAction;

    private Action<InputAction.CallbackContext> onJumpStarted;
    private Action<InputAction.CallbackContext> onJumpHeldStarted;
    private Action<InputAction.CallbackContext> onJumpCanceled;

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();

        moveAction = InputSystem.actions.FindAction("Move");
        jumpAction = InputSystem.actions.FindAction("Jump");

        onJumpStarted = _ => playerController.RequestJump();
        onJumpHeldStarted = _ => playerController.SetJumpHeld(true);
        onJumpCanceled = _ => playerController.SetJumpHeld(false);
    }

    private void OnEnable()
    {
        jumpAction.started += onJumpStarted;
        jumpAction.started += onJumpHeldStarted;
        jumpAction.canceled += onJumpCanceled;
    }

    private void OnDisable()
    {
        jumpAction.started -= onJumpStarted;
        jumpAction.started -= onJumpHeldStarted;
        jumpAction.canceled -= onJumpCanceled;
    }

    private void Update()
    {
        playerController.SetDirection(moveAction.ReadValue<Vector2>());
    }
}
