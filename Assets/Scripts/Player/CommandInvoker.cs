using System.Collections.Generic;

public class CommandInvoker
{
    private readonly Stack<ICommand> history = new();

    public void Execute(ICommand command)
    {
        command.Execute();
        history.Push(command);
    }

    public void Undo()
    {
        if (history.Count > 0)
            history.Pop().Undo();
    }
}