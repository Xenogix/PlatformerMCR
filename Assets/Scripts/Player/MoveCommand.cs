using UnityEngine;
public class MoveCommand : ICommand
{
    private readonly PlayerController playerController;
    private readonly Vector2 newDirection;
    private Vector2 previousDirection;

    public MoveCommand(PlayerController playerController, Vector2 newDirection)
    {
        this.playerController = playerController;
        this.newDirection = newDirection;
    }

    public void Execute()
    {
        previousDirection = playerController.Direction;
        playerController.SetDirection(newDirection);
    }

    public void Undo()
    {
        playerController.SetDirection(previousDirection);
    }
}