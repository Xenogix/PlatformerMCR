# Time Rewind System Design

**Date:** 2026-06-04
**Branch:** `rewind-system` (off `migrate-physics-3d-to-2d`)
**Author/driver:** Claude Code (Opus 4.8) under brainstorming with the user
**Status:** Approved, pending implementation

## Goal

Let the player rewind the world to an earlier state — an **instant jump back**.
On activation the whole simulated world snaps to a point in the recent past and
live play resumes from there. Objects opt in by registering with a central
coordinator; each registered object knows how to serialize and restore *its own*
state compactly.

This is the **Memento pattern** at world scale. Role mapping:

| GoF role   | Type in this system     | Responsibility |
|------------|-------------------------|----------------|
| Caretaker  | `RewindCaretaker`       | Decides *when* to capture/restore; holds the histories; never inspects memento contents. |
| Originator | a **Channel** component  | Knows how to read/write *its own* slice of live state. |
| Memento    | a `TState` value struct | The externalized, allocation-free state token. |

The Caretaker only ever sees an opaque, type-erased history handle (the **narrow
interface**); each Channel has full typed access to its own state (the **wide
interface**). The split is enforced by C# generics + type erasure rather than the
inner-class trick the textbook uses.

## Scope

### In scope

- A `RewindCaretaker` that captures world state on a fixed cadence into bounded
  per-object histories and performs an instant jump-back.
- A `RewindableEntity` lifecycle/identity unit (one per rewindable GameObject)
  coordinating capture/restore/despawn atomically across its channels.
- Two storage strategies, both exercised by the first cut:
  - **Dense** (flat ring, per-tick) — `RigidbodyChannel`.
  - **Sparse** (change-only, carry-forward) — `StateChannel<T>`.
- Spawn/despawn via **deferred destruction + reactivation** (no Factory).
- A **post-restore hook** (`IRewindHook`) the rewind system calls after restoring
  an entity — the seam the future echo/Command system plugs into.

### First-cut cast (priority order)

| Target | Channel(s) | Strategy | Notes |
|---|---|---|---|
| **Player** | `RigidbodyChannel` | dense | pos/rot/linearVelocity/angularVelocity. "Death" already = `Entity.Kill()` → `SetActive(false)`. |
| **Ghost / echo** | `RigidbodyChannel` + `IRewindHook` | dense | The full-lifecycle exemplar: spawns at a tick, self-deactivates at replay end, must deactivate if rewound before its spawn. Its forward replay is the separate Command system; rewind only calls the hook. |
| **Button** | `StateChannel<ButtonState>` | sparse | pressed/unpressed. |
| **Door** | `StateChannel<DoorState>` | sparse | open/close **logical state** restored. Mid-transition animation deferred (see Out of scope). |
| **Pushable obstacle** | `RigidbodyChannel` | dense | dynamic body the player shoves. |

### Out of scope (explicit non-goals)

- **Echo recording & forward replay** — a separate **Command**-pattern spec. This
  document defines only the rewind↔echo *seam* (`IRewindHook`), not the recording
  or replay itself.
- **Hazards** — deferred. When built they will be dynamic-`Rigidbody2D`-driven and
  reuse `RigidbodyChannel` unchanged (real physics ⇒ pos/rot/velocity capture is
  self-sufficient).
- **Animation / transition rendering** — deferred. The first cut restores
  **logical** state only (a door snaps to Open/Closed; no mid-transition pose).
  The enhancement is already seeded: a sparse channel's change-tick *is* the
  transition start, so it will reintroduce per-tick sparse polling + a
  `ticksSinceChange` companion to drive `animator.Play(hash, layer, elapsed/clipLength)`,
  with a `stateHash+normalizedTime` escape hatch for non-derivable animation.
- Smooth reverse playback, scrubbing, interpolation — we jump instantly.
- Deterministic re-simulation after a rewind — restore is a hard state set; live
  physics simply resumes. No determinism guarantee.
- Save-to-disk. Histories are in-memory only.
- Rewind UI/HUD (cooldown, energy). The trigger is a single input.

## Architecture

Two levels below the Caretaker. The Caretaker talks **only** to entities; an
entity coordinates its **channels**. Composition-via-components *is* the "tree".

```
RewindCaretaker (Caretaker)
  • owns the master fixed-step tick
  • registry of RewindableEntity
  • FixedUpdate capture loop + RewindTo(tick)
      │ registers / loops over
      ▼
RewindableEntity            ← identity + lifecycle unit (one per GameObject)
  • stable Id
  • alive record (sparse bool: spawnTick→true, despawnTick→false)
  • discovers its channels (GetComponentsInChildren<IRewindChannel>)
  • atomic Despawn() / RestoreTo() / reclaim
  • after RestoreTo, fires IRewindHook on any present hooks (echo seam)
      │ owns
      ▼
IRewindChannel              ← state carriers (the Originators)
  ├── RigidbodyChannel   (dense)   player, ghost, pushable  (hazards later)
  └── StateChannel<T>    (sparse)  buttons, doors
```

### Why this shape

Every decision traces to a concrete requirement, not speculation:

- **Instant jump-back** removed the need for interpolation and determinism.
- **Spawn + despawn** (the ghost is the live example) drove the entity +
  deferred-destruction model.
- The **economy** requirement (tiny discrete mementos cheap) drove the dense/sparse
  split.
- The **integrity** requirement (despawn must not desync a multi-channel object)
  drove the single per-object coordinator (`RewindableEntity`).

## Contracts

Illustrative shapes; final signatures may shift during implementation.

```csharp
// ── Storage primitives (pure C#, no Unity dependency → edit-mode testable) ──
interface IHistory<T> {
    void Record(int tick, T value);
    T ValueAt(int tick);                  // governing value (carry-forward for sparse)
    void DiscardAfter(int tick);          // truncate the abandoned future on rewind
    void TrimBefore(int windowStartTick); // window eviction; KEEPS the boundary anchor
    bool HasDataInWindow(int from, int to);
}
sealed class DenseHistory<T>  : IHistory<T> where T : struct;                 // flat ring, index = tick offset
sealed class SparseHistory<T> : IHistory<T> where T : struct, IEquatable<T>;  // sorted (tick,value), carry-forward

// ── Channel (Originator), driven by its entity ──
interface IRewindChannel {
    void Capture(int tick);
    void Restore(int tick);
    void DiscardAfter(int tick);
    void TrimBefore(int windowStartTick);
}
abstract class RewindChannel<T> : MonoBehaviour, IRewindChannel where T : struct {
    protected abstract T    Read();         // read live state
    protected abstract void Write(T value);  // apply restored state
}

// ── Post-restore seam for the future Command/echo system ──
interface IRewindHook { void OnRestored(int tick); } // e.g. echo resets its replay cursor / truncates recording
```

> **Deferred:** mid-transition animation needs the *age* of a carried-forward value
> (`ticksSinceChange`) plus per-tick sparse polling. Both arrive with the animation
> enhancement; the first cut restores logical state only.

> **Unity caveat:** open generic `MonoBehaviour`s aren't inspector-serializable,
> so `StateChannel<T>` ships as concrete subclasses
> (`ButtonChannel : StateChannel<ButtonState>`, `DoorChannel : StateChannel<DoorState>`).

## State model & invariants

- **Existence rule:** an entity exists at tick T **iff** its alive record carries
  `true` at T (default `false` before its first entry). One rule covers both "not
  spawned yet" and "already despawned" — no special cases. *The ghost is the
  motivating case: rewinding before its spawn tick deactivates it.*
- **Deferred destruction:** a gameplay "despawn" calls `entity.Despawn()` →
  records `alive=false` and **deactivates** the GameObject; it is **never**
  `Destroy()`-ed during play, so rewind reactivates the retained instance (no
  Factory). `Entity.Kill()` (already `SetActive(false)`) is exactly this hook.
- **Reclamation:** a dormant entity is *actually* `Destroy()`-ed (and deregistered)
  only once it has no `alive=true` anywhere in the window — it can never again be a
  rewind target. Bounds retained-object memory.
- **Discard-after-T:** on rewind to T, every history truncates entries after T; the
  abandoned future is gone and new history records forward from T.
- **Opt-in / time-travel-resistant:** registering an entity is the opt-in to time.
  An object with **no** `RewindableEntity` lives outside time and conserves its
  state across every rewind — correct and intended for score, death/attempt counts,
  achievements. The only bug class is an *intent/registration mismatch*.

## Capture & lookup cadence

- The Caretaker increments a tick every `FixedUpdate` (physics-aligned ⇒ velocities
  captured consistently).
- A single capture pass runs every **N** fixed steps (`captureEveryNSteps`,
  default ≈ 5 → ~0.1 s @ 50 Hz, the memory/CPU knob): dense channels record their
  per-tick state, sparse channels record-if-changed (equality-suppressed via
  `IEquatable<T>`).
- Rewind targets snap to the nearest **capture tick ≤ desired tick**; a sparse value
  is read there by carry-forward, giving the correct logical state at the target.
  Because targets land on capture ticks, dense-cadence sparse capture is sufficient
  for logical correctness — sub-cadence change-tick precision is only needed for the
  deferred animation offset.

## Data flow

- **Capture:** `tick++` each fixed step; every N-th step run one capture pass —
  dense channels record, sparse channels record-if-changed — then window eviction +
  reclamation.
- **Spawn:** `Instantiate` → the `RewindableEntity` registers and discovers its
  channels; its first captured tick is its birth tick (`alive=true`). (Ghosts spawn
  here.)
- **Despawn:** gameplay calls `entity.Despawn()` (not `Destroy`): record
  `alive=false`, deactivate, mark dormant, stop capturing. (Ghosts self-despawn at
  replay end; the player despawns via `Kill()`.)
- **Rewind (fixed offset):** `target = nearestDenseTick(now − rewindOffset)`. For
  each entity: read `alive@target` → if true, reactivate + `Restore(target)` all
  channels, then fire `IRewindHook.OnRestored(target)`; if false, deactivate. Then
  `DiscardAfter(target)` everywhere, set the master tick to `target`, and reclaim
  entities with no existence at/before it.
- **Reclaim:** any dormant entity with no `alive=true` left in the window is
  `Destroy()`-ed and deregistered.

### Emergent causality (no special handling)

Because every object independently captures its own state, cross-object causality
restores correctly for free:

- **Button → door:** rewinding before a press un-presses the button *and* re-closes
  the door — each was captured independently.
- **Pushable resting on a button:** both rewind independently, staying consistent.

## Physics / velocity specifics

Velocity is **not** on the `Transform` (position/rotation/scale only) — it lives on
the `Rigidbody2D`. On Unity 6 the properties are `linearVelocity` (`Vector2`) and
`angularVelocity` (`float`, °/s); the old `.velocity` is deprecated.

- Physics-driven objects (player, pushable, future hazards) use **`RigidbodyChannel`**:
  capture `rb.position`, `rb.rotation`, `rb.linearVelocity`, `rb.angularVelocity`.
- **Capturing only the Transform on a dynamic body is the trap:** rewind would
  teleport it with zero velocity, so a falling player would briefly hang. Velocity
  is state.
- Restore is a **hard set** (instant jump ⇒ no determinism needed): write
  position/rotation/velocity directly.

### Reactivation hygiene

When restoring (especially reactivating a dormant body), reset transient state so
the object resumes validly on the new timeline: reset `Rigidbody2D` interpolation;
`Sleep()`/wake as appropriate; restart/clear any in-flight coroutines.

## Ghost / echo seam

The echo's recording and forward replay are the **separate Command spec**. This
spec treats a ghost as:

1. an ordinary `RewindableEntity` + `RigidbodyChannel` (rewinds like any body), and
2. a holder of an `IRewindHook`: after the rewind system restores the ghost to tick
   T, it calls `OnRestored(T)`, which the echo component implements to **reset its
   replay cursor** to T (or **truncate an in-progress recording** at T).

The rewind system thus stays unaware of Command details; the echo plugs into one
documented callback. Its spawn/self-deactivate/reclaim behaviour is governed
entirely by the existence rule above — no ghost-specific lifecycle code.

## Configuration & defaults

| Setting | Default | Notes |
|---|---|---|
| `captureEveryNSteps` (dense cadence) | 5 (~0.1 s @ 50 Hz) | sparse is always per-tick |
| Rewind window | configurable capacity; default ~5 min of ticks | "unbounded" allowed (see Memory) |
| `rewindOffsetSeconds` | tunable (e.g. 3 s) | fixed jump-back distance per activation |
| Player rewindable | yes | ordinary `RewindableEntity` + `RigidbodyChannel` |

## Memory considerations

- **Sparse** channels grow only with the number of *changes* — negligible
  (buttons/doors change rarely).
- **Dense** channels are the cost driver: `frames ≈ playSeconds / 0.1 s`. At
  ~32 bytes/frame, 5 min ≈ ~96 KB per dynamic body; a handful of bodies (player,
  ghost, pushables) stays well under a megabyte.
- A truly **unbounded** window means dense memory grows with the longest stretch of
  *un-rewound* forward play (a rewind's `DiscardAfter` reclaims the abandoned
  future). Fine for platformer-length levels.
- **Watch-point:** heavy spawning of dense-channel objects over a long unbounded
  window is the one place memory could bite. Mitigations if it ever does: cap the
  window, or give such objects cheaper channels. Documented so the cost is never
  silent.

## Edge cases & risks

- **Window-boundary anchor (sparse):** `TrimBefore` must *keep* the last entry
  before the window start (a door `Open`ed long ago still governs "now"). Naïve
  "drop everything older than the window" loses the carried-forward value.
- **Float jitter:** never put continuous floats on a sparse channel — a resting body
  jitters ~1e-7/tick, defeating change-detection. Continuous → dense always.
- **Equality without boxing:** sparse `TState` implements `IEquatable<T>`.
- **Intent/registration mismatch:** the framework can't detect "I meant to rewind
  this." Mitigation: review discipline + clear per-prefab channel conventions.
- **Reactivation correctness:** a reactivated object must end fully valid (see
  Reactivation hygiene) — the most likely source of subtle bugs.
- **Echo seam ordering:** `IRewindHook.OnRestored` must fire **after** channels are
  restored, so the echo resets its cursor against an already-correct pose.

## Testing strategy

- **Edit-mode unit tests** (pure C#, no runtime) — highest value, on the storage
  primitives:
  - `SparseHistory`: carry-forward lookup; change-suppression via `IEquatable`;
    `TrimBefore` keeps the boundary anchor; `DiscardAfter` truncates the tail.
  - `DenseHistory`: per-tick round-trip; nearest-≤ lookup; `DiscardAfter`.
- **Play-mode tests** on lifecycle & integration:
  - spawn → capture → despawn → rewind → reactivate (existence rule);
  - rewind before a ghost's spawn → ghost deactivated; reclaim after aging out;
  - `RigidbodyChannel`: a body restored mid-fall keeps falling (velocity restored);
  - a door opened then rewound restores the correct open/closed state;
  - button → door causality holds across a rewind;
  - a non-registered counter keeps its value across a rewind (time-travel-resistant);
  - `IRewindHook.OnRestored` fires after channel restore (stub hook records order).

## Implementation phasing

1. Storage primitives (`DenseHistory`, `SparseHistory`) + edit-mode tests.
2. `IRewindChannel` + `RewindChannel<T>`; `RigidbodyChannel`; `StateChannel<T>`.
3. `RewindableEntity` (alive record, channel discovery, Despawn/RestoreTo, reclaim,
   `IRewindHook` dispatch).
4. `RewindCaretaker` (tick, registry, FixedUpdate capture, `RewindTo`) + rewind input.
5. Wire the cast in priority order: player → ghost (target+seam) → button+door →
   pushable. Hazards later.

## Patterns summary (for the report)

- **Memento** is the backbone: Caretaker = `RewindCaretaker`, Originators =
  Channels, Mementos = `TState` structs; wide/narrow interface preserved via
  generics + type erasure.
- Extensions beyond vanilla Memento, each justified by a requirement:
  multi-originator time-indexed history; sparse change-only storage; and object
  lifecycle (entity coordinator + deferred destruction + reclamation), which the
  pattern itself does not address.
- **Command** is reserved for the *future* echo recording/replay subsystem — a
  distinct goal, joined to rewind only through the `IRewindHook` seam.

## Open questions

None blocking. Defaults above are tunable during implementation.
