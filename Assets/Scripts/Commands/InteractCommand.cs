public class InteractCommand : ICommand
{
    private readonly InteractionDetector detector;

    public InteractCommand(InteractionDetector detector)
    {
        this.detector = detector;
    }

    public void Execute(Player target)
    {
        detector.GetClosest()?.Interact();
    }
}