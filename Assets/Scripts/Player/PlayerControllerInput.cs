using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerController))]
public class PlayerControllerInput : MonoBehaviour
{
    private PlayerController playerController;

    private InputAction moveAction;
    private InputAction jumpAction;

    private void Start()
    {
        playerController = GetComponent<PlayerController>();

        moveAction = InputSystem.actions.FindAction("Move");
        jumpAction = InputSystem.actions.FindAction("Jump");
    }

    private void Update()
    {
        playerController.Move(moveAction.ReadValue<Vector2>());
        if (jumpAction.WasPressedThisFrame())
            playerController.Jump();
    }
}
