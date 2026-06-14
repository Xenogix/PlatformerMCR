# The Command Pattern in PlatformerMCR

Presentation material — how we used the pattern, the choices we made, the strengths
we exploited, what we left out, and how we adapted it.

Diagrams: `uml.sly` (class diagram, edited in Slyum → `command.pdf`) and
`command-pattern-sequence.puml` (record → slice → replay). Regenerate the sequence
with `plantuml -tsvg command-pattern-sequence.puml`.

---

## Slide 1 — Why Command? The game mechanic that demanded it

PlatformerMCR's core mechanic: open a timeline, scrub back to a past tick, and **split
off a clone ("echo")** that replays exactly what you did, while you keep playing.
Cooperate with your past self (hold a lever, weigh down a plate...).

The requirement that drives everything: *what the player did* must exist as **data** —
storable, sliceable, re-executable on another body. That is precisely the Command
pattern's job: **turn an action into an object**.

> Speaker note: start from the gameplay clip, then ask "what does the engine need to
> remember to make this work?" — the answer (the inputs, as objects) introduces the pattern.

---

## Slide 2 — Textbook recap (GoF)

> "Encapsulate a request as an object, thereby letting you parameterize clients with
> different requests, queue or log requests, and support undoable operations."

Canonical roles: **Command** (interface with `Execute()`), **ConcreteCommand** (binds a
*Receiver* + arguments), **Invoker** (triggers commands), **Receiver** (does the work),
**Client** (creates and configures commands).

Classic motivations: menu items/buttons, undo/redo stacks, request queues, macro commands.

---

## Slide 3 — Role mapping: GoF vs. our implementation

| GoF role | Canonical form | PlatformerMCR |
|---|---|---|
| Command | `Execute()` | `ICommand.Execute(Player target)` — receiver is a **parameter** |
| ConcreteCommand | binds receiver + args | `MoveCommand(dir)`, `JumpCommand`, `JumpHeldCommand(held)`, `UseCommand` — **args only, no receiver** |
| Invoker | one (button, menu) | **Two**: `PlayerCommandInvoker` (live play) and `ClonePlayback` (replay) |
| Receiver | any object | `Player` facade → delegates to `PlayerController` / `InteractionDetector` |
| Client | wires commands up | `PlayerCommandInvoker` creates from input; `RewindDirector` re-targets the recording at a clone |
| History list | undo stack | `CommandTimeline` — a **replay log**, not an undo stack |

All command code lives in `Assets/Scripts/Commands/` (~7 small files).

---

## Slide 4 — Adaptation #1 (the big one): receiver passed to `Execute`, not stored

```csharp
public interface ICommand
{
    void Execute(Player target);
}
```

GoF stores the receiver inside the ConcreteCommand. We pass it as a parameter.
Consequence: **one recording, many targets** — the *exact same command instance* is
executed on the live player during play, then re-executed on a clone during replay.
No copying, no re-binding, no translation layer.

This is the variant Nystrom recommends in *Game Programming Patterns* (Command chapter)
for exactly this use case: "pass in the actor" → the same input stream can drive the
player, an AI, or a replay ghost.

---

## Slide 5 — How a live tick works (invoker)

`PlayerCommandInvoker.Tick(tick, dt)` — driven by `GameClock` at fixed ticks:

```csharp
List<ICommand> changed = null;
if (!hasRecorded || move != lastMove)
    (changed ??= new List<ICommand>()).Add(new MoveCommand(move));
if (!hasRecorded || jumpHeld != lastJumpHeld)
    (changed ??= new List<ICommand>()).Add(new JumpHeldCommand(jumpHeld));
if (jumpPressedThisTick) (changed ??= new List<ICommand>()).Add(new JumpCommand());
if (usePressedThisTick)  (changed ??= new List<ICommand>()).Add(new UseCommand());

if (changed != null)
    foreach (ICommand cmd in changed) cmd.Execute(player); // 1) drive live player
controller.Tick(tick, dt);                                 // 2) advance physics
Timeline.Record(tick, changed);                            // 3) record
```

Key detail: discrete presses are **latched** in input callbacks (`jumpPressedThisTick`)
and consumed on the next fixed tick — so the recording is *exactly* what was executed,
even for presses landing between two ticks.

---

## Slide 6 — Adaptation #2: sticky vs. discrete commands + sparse recording

```csharp
public interface IStickyCommand : ICommand { }   // marker interface
```

- **Sticky** (`MoveCommand`, `JumpHeldCommand`): effect persists until changed →
  recorded **only on change**; in between, the controller carries the state forward.
- **Discrete** (`JumpCommand`, `UseCommand`): one-shot → recorded at every occurrence.

`CommandTimeline` therefore stores a `TickRecord {Tick, List<ICommand>}` only on
*change ticks* (sparse), addressed by **absolute tick** via binary search — never by
list position. Most ticks store nothing: per-tick allocations are near zero even
though every action is a heap-allocated command object.

A naive "one command per tick per input" log at 50 ticks/s would allocate thousands of
objects per minute of play, per timeline. Sparseness is what made the pattern viable.

---

## Slide 7 — Adaptation #3: slicing the history at the rewind point

On clone split at tick T (`RewindDirector`):

```csharp
CommandTimeline echoScript = livePlayer.Timeline.SliceFromTick(target);
livePlayer.Timeline.TruncateAfterTick(target - 1);
```

`SliceFromTick(T)` hands the clone a frozen `[T, end]` copy — **with the latest sticky
command of each kind re-established at T**. If you were mid-run when the split happens,
the clone gets a synthesized `MoveCommand` at its first tick so it resumes mid-stride.
The live player keeps `[.., T-1]` and re-records forward.

This is where sparse recording bites back: a slice that starts between two change
ticks would otherwise begin with *no* movement state. The sticky-marker interface
exists to solve precisely this.

---

## Slide 8 — Replay: the second invoker

`ClonePlayback.Tick(tick, dt)` — same clock, same cadence:

```csharp
TickRecord record = timeline.GetAtTick(tick);
if (record != null)
    foreach (ICommand cmd in record.Commands)
        cmd.Execute(player);      // the CLONE's Player — same command objects
controller.Tick(tick, dt);
```

The clone is a **full `Player`** with the same `PlayerController`, same physics, same
mass. Replay is not an animation: the echo genuinely re-runs the simulation, fed by
recorded intent. When `tick > timeline.LastTick`, the echo retires itself.

Determinism prerequisites (not provided by Command itself):
- fixed-tick `GameClock` (commands stamped with absolute tick numbers);
- strict tick ordering: state observers capture **before** movers act;
- full state snapshot restored at the split point (see Slide 10).

---

## Slide 9 — Strengths of the pattern we actually exploited

1. **Actions become objects** — they can be stored, addressed by tick,
   sliced, truncated, handed to another body. The entire echo mechanic is "the
   history list from GoF, pointed at a different receiver".
2. **Decoupling input from action** — the invoker knows input, receivers know
   physics/interaction; neither knows the other's details. Replay needed *zero*
   changes to the receivers.
3. **Single code path, live and replayed** — the same `Execute` runs in both modes,
   so live/replay divergence bugs are structurally impossible at the command level.
4. **Open/closed in practice** — the `Use` action was added *after* the recording
   system existed: one new 15-line class + 2 lines in the invoker. Levers/doors then
   worked in replays *for free* (git: `feat(player): tick-driven command system` →
   `feat(interactables): port lever/door/Use system`).
5. **Logging as a debugging tool** — the timeline doubles as a perfect input trace of
   a play session.

---

## Slide 10 — What we did NOT use, and the pattern's limits

**Not used (deliberately):**
- **`Undo()` on commands.** Rewind is not command-undo: physics is not invertible
  (you cannot "un-execute" a jump in a dynamics simulation — friction, collisions and
  integration lose information). Undo is delegated to a **Memento** system:
  `RewindCaretaker` + per-entity `RewindChannel<T>` snapshot state on a cadence, and
  rewind = restore snapshot + discard later history. Command logs *intent forward*;
  Memento restores *state backward*. Each pattern does the half it is good at.
- **Macro/composite commands** — a multi-action tick is just a `List<ICommand>`.
- **Queueing / deferred execution** — commands execute the same tick they are created;
  the "queue" benefit of GoF is unused.
- **Persistence** — timelines are in-memory, per-try; no serialization (though pure-data
  commands would make it trivial — a natural extension).
- **Validation in commands** — commands are unconditional; preconditions (grounded,
  coyote time...) live in the receiver. Commands stay dumb data.

**Intrinsic limitation we hit:** a command log replays *intent*, not *outcome*. If the
world diverges (you push a box into the echo's path), the same commands produce
different results.

---

## Slide 11 — Turning the limitation into the game

`UseCommand` is context-free — it does not record *which* lever was used:

```csharp
public void Use() => interactor?.GetClosest()?.Interact();
```

The replaying clone interacts with **whatever is closest at its position at that
tick**. We chose to replay intent rather than bind outcomes, accepted the divergence
risk, and contained it with determinism (fixed tick + snapshot restore at the split
point ⇒ an undisturbed echo replays perfectly). Residual divergence — the player
interfering with their own echo — *is the puzzle mechanic*.

> Speaker note: this is the strongest "design choice" moment of the talk — the classic
> weakness of command-replay, deliberately embraced instead of engineered away.

---

## Slide 12 — Takeaways

- Command earned its place here: the mechanic *is* the pattern (a re-targetable action log).
- We kept commands **minimal**: an interface, four tiny classes, no base class, no undo,
  no receiver field — every omission was a decision, not an oversight.
- The adaptations that mattered: **receiver-as-parameter** (one recording, N targets),
  **sticky/discrete split + sparse log** (memory), **tick-addressed slicing** (clone splits).
- Command doesn't work alone: **Memento** (snapshots/rewind) and a fixed-tick clock
  supply the determinism and reversibility that Command cannot.
- Patterns are menus, not contracts: we used `letting you parameterize clients with
  different requests` and `log requests`, and consciously skipped `queue` and `undoable`.

---

## Appendix — file map

| File | Role |
|---|---|
| `Assets/Scripts/Commands/ICommand.cs` | `ICommand`, `IStickyCommand` |
| `Assets/Scripts/Commands/{Move,Jump,JumpHeld,Use}Command.cs` | concrete commands |
| `Assets/Scripts/Commands/PlayerCommandInvoker.cs` | live invoker: input → commands → execute + record |
| `Assets/Scripts/Commands/CommandTimeline.cs` | sparse history; `Record/GetAtTick/SliceFromTick/TruncateAfterTick` |
| `Assets/Scripts/Commands/TickRecord.cs` | `{Tick, List<ICommand>}` |
| `Assets/Scripts/Commands/ClonePlayback.cs` | replay invoker on echoes |
| `Assets/Scripts/Player/Player.cs` | receiver facade (`Move/Jump/SetJumpHeld/Use`) |
| `Assets/Scripts/Commands/InteractionDetector.cs` | finds the nearest `IInteractable` for `Use()` |
| `Assets/Scripts/Rewind/RewindDirector.cs` | clone split: slice + truncate + spawn echo |
| `Assets/Scripts/Rewind/RewindCaretaker.cs`, `Rewind/Channels/*` | the Memento side (snapshots, rewind) |
| `Assets/Scripts/Time/GameClock.cs` | fixed ticks, observers-before-movers ordering |
