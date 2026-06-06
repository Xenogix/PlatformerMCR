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

/// <summary>
/// Marks a command whose effect PERSISTS until changed — movement direction, jump-held.
/// These are recorded only when they change and carried forward in between (sparse logging),
/// and on rewind a slice re-establishes the latest sticky command of each kind at its start
/// so a replaying clone resumes mid-stride. Discrete one-shot commands (jump press, use) are
/// NOT sticky.
/// </summary>
public interface IStickyCommand : ICommand { }
