public class InteractCommand : ICommand
{
    private readonly InteractionDetector detector;

    public InteractCommand(InteractionDetector detector)
    {
        this.detector = detector;
    }

    public void Execute()
    {
        detector.GetClosest()?.Interact();
    }

    public void Undo() { } // will be filled in with Memento later
}