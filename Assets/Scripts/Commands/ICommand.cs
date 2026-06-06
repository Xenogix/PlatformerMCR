/// <summary>
/// The Command pattern's core abstraction: one player action, captured as an object.
///
/// The receiver (<see cref="Player"/>) is passed to <see cref="Execute"/> rather than
/// stored in the command. That is the whole trick behind the rewind/clone mechanic:
/// the exact same recorded command instance is executed on the live player while
/// playing, then later executed on a *clone* during replay — same command, different
/// target.
/// </summary>
public interface ICommand
{
    void Execute(Player target);
}
