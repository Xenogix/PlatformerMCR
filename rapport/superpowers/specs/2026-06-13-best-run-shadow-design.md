# Best-Run Shadow — Design

**Date:** 2026-06-13
**Branch:** `feature/best-run-shadow`
**Status:** Approved, pending implementation plan

## Goal

When the player finishes a level faster than any prior run, persist the *entire*
performance — the player plus every clone that was alive — as per-tick position
tracks. On re-entering that level, dark/translucent **shadow** replays of all those
bodies retrace the best run alongside the live attempt. Records are saved to disk and
survive across game sessions.

"Best" is decided by **TC (finish tick)**: the lower the `GameClock.Tick` at which the
finish flag is touched, the tighter the run. Both HUD timers (RT and TC) still display
unchanged; TC only decides what gets saved.

## Background — what already exists

The codebase already records and replays runs; this feature reuses that data rather than
adding a parallel recorder.

- **Recording.** Every actor body (the live player and each spawned clone) is a
  `RewindableEntity` carrying a `RigidbodyChannel`, which captures
  `RigidbodyState { Position, Rotation, LinearVelocity, AngularVelocity }` every tick into
  a `DenseHistory<RigidbodyState>`.
- **Unlimited history.** `RewindCaretaker` runs `captureRate: 1`, `windowSeconds: 0` (no
  eviction). On rewind, `DiscardAfter` trims and the player re-records forward, so at
  finish each body holds its *resolved* monotonic per-tick history from its spawn tick to
  the finish. A run can never outlive the (effectively infinite) window, so a best run is
  always fully captured.
- **Clock origin.** `GameClock` is created per scene and starts at tick 0 each level load,
  so absolute ticks are already level-relative. The same tick origin applies to a saved run
  and a future live run, so position tracks align by absolute tick with no normalization.
- **Replay reference.** `ClonePlayback : ITickable` already replays a recording onto a
  clone, addressed by absolute clock tick (so a rewind rewinds the replay for free). The
  shadow replay mirrors this pattern but writes positions directly instead of re-executing
  commands through physics.
- **Finish hook.** `FinishFlag.OnTriggerEnter2D` fires while the scene is fully alive, then
  routes to `LevelTransition.PlayOutroThenLoad(LevelLoader.LoadNext)`. This is where the
  final RT/TC are frozen — the natural place to rank and save the run.

### Why positions, not commands

`ClonePlayback` re-simulates recorded commands through physics, which is *non-deterministic
forward* and intentionally drifts. A faithful best-run shadow must retrace the exact path,
so we replay stored **positions** kinematically — exact, zero drift.

## Architecture

### Components (new)

- **`ShadowRecord`** (plain data) — `int BestTick`, `List<ShadowTrack> Tracks`.
  **`ShadowTrack`** — `int SpawnTick`, `List<Vector2> Positions` (one per consecutive tick
  from `SpawnTick`).
- **`ShadowStore`** (static) — file persistence under
  `Application.persistentDataPath/ghosts/<sceneName>.ghost`.
  - `bool TryLoad(string levelKey, out ShadowRecord record)`
  - `void Save(string levelKey, ShadowRecord record)`
  - Compact binary: `magic + version + bestTick + trackCount + per track (spawnTick, count,
    (x,y) floats)`. Version mismatch / parse error / missing file → returns false (treated
    as "no record"); the file is overwritten on the next winning run. IO failure on save
    logs a warning and never throws into gameplay.
- **`BestRunRecorder`** — invoked from `FinishFlag` at the finish line. Reads
  `finishTick = GameClock.Tick`, loads the stored record for the level, and if there is no
  record or `finishTick < stored.BestTick`, collects every actor body's track and saves.
- **`ShadowDirector`** (MonoBehaviour in the level, sibling of `RewindDirector`) — on
  `Start`, `TryLoad`s the record and, for each track, instantiates the shadow prefab and
  hands its `Positions` + `SpawnTick` to a `ShadowPlayback`.
- **`ShadowPlayback : ITickable`** — one per shadow body. Registers as a `GameClock` mover.
  Each tick: `i = tick - SpawnTick`; in range → activate + `transform.position =
  Positions[i]`; out of range → deactivate the renderer. Pure position write — no physics,
  no colliders. Clock-tick addressed, so a live-attempt rewind rewinds the shadow for free.

### Components (modified)

- **`DenseHistory<T>`** — add read-out accessors so a finished run can be exported:
  `int BaseTick`, `int Count`, and `T ValueAt(int i)` (index `i` is the entry at tick
  `BaseTick + i`, since `step == 1`). `SparseHistory` is untouched.
- **`RewindChannel<T>`** — expose the owned history to subclasses (e.g.
  `protected IHistory<T> History`) so a concrete channel can export it.
- **`RigidbodyChannel`** — add `List<Vector2> ExportPositions(out int spawnTick)` that maps
  its dense `RigidbodyState` history to positions, with `spawnTick = BaseTick`.
- **`FinishFlag`** — before invoking the transition, call the `BestRunRecorder` (synchronous,
  scene still alive).

### Asset

- **`PlayerShadow.prefab`** — a minimal *visual-only* prefab that reuses the player/echo mesh
  (e.g. built by duplicating the echo and stripping it down) with a dark, more-transparent
  shared material. It has **no `Rigidbody2D`, no collider**, and none of the
  `Player` / `PlayerController` / `ClonePlayback` / `PlayerCommandInvoker` / `RewindableEntity`
  scripts (a true prefab *variant* can't shed those — their `RequireComponent` chains forbid
  it — so this is a fresh prefab carrying the same visual). It holds only the renderer(s) and
  a root `ShadowPlayback`, which writes the root `transform.position`. All shadows look
  uniformly dark — clearly distinct from the colorful live clones.
- **Wiring** — add a `ShadowDirector` to the level (mirroring how `RewindDirector` is placed),
  with the shadow prefab assigned.

## Data flow

1. **Level load** → `ShadowDirector.Start` → `ShadowStore.TryLoad(sceneName)`.
   If a record exists, instantiate one shadow per track and start its `ShadowPlayback`.
2. **Play** → existing recording/rewind/clone systems run unchanged; shadows replay by tick.
3. **Finish** → `FinishFlag.OnTriggerEnter2D` → `BestRunRecorder`:
   `finishTick = GameClock.Tick`; if better than stored (or none), enumerate actor bodies
   (`FindObjectsByType<Player>(include-inactive)`), export each body's `(spawnTick,
   positions)` from its `RigidbodyChannel`, build a `ShadowRecord`, `ShadowStore.Save`.
   Then the level transition proceeds as before.

## Body enumeration

`FindObjectsByType<Player>(FindObjectsInactive.Include)` returns the live player and every
clone — including clones that despawned (retained, inactive, full history intact). Doors,
levers, and pushables are excluded because they carry no `Player` component. Each returned
body has a `RigidbodyChannel` to export.

## Decisions

- **Position only** — no rotation/velocity in the shadow (matches the request).
- **Best = finish tick (TC)** — deterministic and the same axis the shadow replays along.
- **Level key = active scene name** — each level is its own scene; stable at runtime
  (no `AssetDatabase` dependency).
- **v1: shadows freeze during timeline scrubbing.** They are `GameClock` movers, and movers
  don't tick while the clock is paused for scrubbing, so a shadow holds its last pose while
  the timeline is open. Acceptable for v1 (scrubbing is a planning mode, not the race). An
  upgrade path — a `Preview(tick)` hook the director calls so shadows follow the scrub — is
  left for later.

## Error handling

- No record file / corrupt / wrong version → no shadows; gameplay unaffected.
- Save IO failure → logged warning, no throw into gameplay.
- A level never finished → no file, no shadows.
- Player-only run → single track; multi-clone run → multiple tracks. Both handled by the
  list shape.

## Testing

- **Pure C# unit tests** (no Unity scene needed):
  - `ShadowStore` round-trip: `Save` then `TryLoad` reproduces `BestTick` and every track's
    `SpawnTick` and `Positions`.
  - Version/corruption handling: a wrong-magic or truncated file → `TryLoad` returns false.
  - Ranking: a worse `finishTick` does not overwrite; a better one does.
  - `DenseHistory` export: base tick + values match what was recorded, including after a
    `DiscardAfter` (rewind) trim.
- **Play-mode / manual:**
  - Finish a level → `<sceneName>.ghost` appears under `persistentDataPath/ghosts`.
  - Re-enter → shadows spawn and retrace the player + clones exactly.
  - Beat the time → file updates; finish slower → file unchanged.
  - Restart the editor/app → shadows still load (cross-session persistence).

## Out of scope (v1)

- Scrub-correct shadows (freeze accepted, see Decisions).
- Rotation/velocity replay.
- Any HUD surfacing of the stored best time beyond the existing timers.
- Per-shadow distinct coloring (all uniformly dark).
