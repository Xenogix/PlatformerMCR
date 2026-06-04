using UnityEngine;
public class JumpCommand : ICommand
{
    private readonly PlayerController playerController;

    public JumpCommand(PlayerController playerController)
    {
        this.playerController = playerController;
    }

    public void Execute()
    {
        playerController.RequestJump();
        playerController.SetJumpHeld(true);
    }

    public void Undo()
    {
        playerController.SetJumpHeld(false);
    }
}