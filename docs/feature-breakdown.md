# Feature breakdown

A map of the game's systems: what each feature is, **where it lives**, and **how it's implemented** — with the design choices called out where there was a real fork in the road.

The game is a CRT‑TV‑themed puzzle‑platformer whose headline mechanic is **rewind + clones ("echoes")**: you scrub time back, leave a clone replaying your past inputs, and stack clones to solve puzzles (open doors, weigh switches, etc.). Levels are "channels"; transitions are TV static.

---

## 1. Time & ticking — the spine

| File | Role |
|------|------|
| `Assets/Scripts/Time/GameClock.cs` | Single source of game time; one integer tick per `FixedUpdate`. |
| `Assets/Scripts/Time/ITickable.cs` | What the clock drives. |

Everything deterministic hangs off a single integer `Tick`. Each fixed step the clock ticks two groups **in order**:

1. **Observers** (the rewind caretaker) — snapshot the world *first*.
2. **Movers** (player invoker, clone playbacks) — set velocities for this tick.

**Choice — capture the *entering* state (observers before movers).** Snapshotting before the movers act records the position/velocity the tick is *about* to consume. That pairing is internally consistent, so restoring it and re‑running the tick reproduces it exactly. Capturing *after* the movers would store a velocity already advanced by this tick's gravity, which a rewind would then advance a second time (an extra tick of gravity per commit). The mover group is sorted bottom‑up by height so a carrier ticks before its rider.

**Choice — `Tick++` in a `finally`** (added this round). A throwing tickable used to skip the increment and freeze game time forever; the `finally` guarantees time advances regardless.

---

## 2. Rewind system — Memento, generalized

| File | Role |
|------|------|
| `Assets/Scripts/Rewind/RewindCaretaker.cs` | Owns the registry of rewindables + the capture cadence; performs `Preview`/`Commit`. Rides the clock as an observer. |
| `Assets/Scripts/Rewind/RewindableEntity.cs` | One rewindable object: its channels **and** its existence ("alive record"). |
| `Assets/Scripts/Rewind/Channels/RewindChannel.cs` (+ `IRewindChannel`) | A rewindable slice of an object; owns its history, delegates Read/Write. |
| `…/Channels/RigidbodyChannel.cs` | Position + rotation + both velocities for a physics body. |
| `…/Channels/ToggleableChannel.cs` | On/off state for an interactable (see §7). |
| `…/History/IHistory.cs`, `DenseHistory.cs`, `SparseHistory.cs` | The two storage strategies. |

**Memento + Strategy.** A channel is the originator/memento; the *storage logic* lives behind `IHistory<T>` so a channel just picks a strategy:

- **`DenseHistory`** — one value per cadence tick, addressed by `baseTick + i*step`, O(1) and no per‑entry tick stored. Used for continuous data (rigidbody pose). **Choice:** dense because it changes every tick anyway.
- **`SparseHistory`** — change‑only with carry‑forward, binary‑searched. Used for rarely‑changing flags (alive, lever/door state). **Choice:** sparse to keep per‑tick allocation near zero.

**Choice — deferred destruction + an "alive record" instead of `Destroy`.** A despawned object is **deactivated and retained**, and its existence is a `SparseHistory<bool>` on the entity. Existence rule: an entity exists at tick T iff its alive record carries `true` at T (default `false` before its first capture). Rewinding past a death reactivates the retained instance; an object only gets truly `Destroy`‑ed once it's dormant *and* its last alive‑change has aged out of the window (it can never be a rewind target again). This is why doors **don't** use `SetActive` (see §7) — that collided with this dormancy.

**Choice — asymmetric discard on commit.** `Commit(tick)` restores everyone, but only `DiscardAfter(tick)` the histories of entities **alive at the target**. An entity *not yet born* at the target keeps its future, so when the clock replays forward into its birth it can be revived — otherwise a clone you rewound past its own spawn would be erased.

**Fix this round — revive corruption.** `PrepareCapture` (revive) clears the dense channels but the alive record is sparse; the immediately‑following `Capture` would append `true@birth` *after* a later `false@despawn`, breaking the change‑only history's ascending‑tick invariant (its binary search then misreads). Fixed by `DiscardAfter(tick)`‑ing the alive record on revive. (Only bit after rewinding past a *finished* clone — "works in most cases.")

---

## 3. Command pattern — input as objects

| File | Role |
|------|------|
| `Assets/Scripts/Commands/ICommand.cs` | `Execute(Player)` + the `IStickyCommand` marker. |
| `MoveCommand` / `JumpHeldCommand` / `JumpCommand` / `UseCommand` | The concrete actions. |
| `Assets/Scripts/Commands/PlayerCommandInvoker.cs` | Turns input into commands each tick, executes them, records them. |
| `Assets/Scripts/Commands/CommandTimeline.cs` (+ `TickRecord`) | The recording. |

**Choice — receiver passed to `Execute`, not stored.** The same recorded command instance runs on the **live** player while playing and later on a **clone** during replay — same command, different target. That retargeting is the whole trick behind clones.

**Choice — sparse recording with sticky vs discrete.** Continuous state (`Move`, `JumpHeld`) is `IStickyCommand`: recorded only when it *changes* and carried forward; discrete one‑shots (`Jump`, `Use`) are recorded only on the tick pressed. Most ticks store nothing. On a rewind‑split, `CommandTimeline.SliceFromTick` re‑establishes the latest sticky command of each kind at the slice start, so a clone resumes mid‑stride.

**Fixes this round (echo‑input bleed):**
- The invoker's change‑detection baseline (`lastMove`/`lastJumpHeld`) wasn't reset on rewind, so a resume input that *looked* unchanged got suppressed and the timeline carried a stale sticky value forward — a clone sliced from there replayed the wrong movement. Now it re‑emits the sticky state on a backward tick.
- `RewindDirector.ConfirmRewind` truncated commands to `target` while the clock resumes *at* `target` and re‑records it → two frames at one tick (binary search ambiguity). Now truncates to `target‑1`.
- Press latches are cleared **before** `Execute`, so a throwing command can't leave a press latched and re‑fire every tick.

---

## 4. Clones / echoes

| File | Role |
|------|------|
| `Assets/Scripts/Rewind/RewindDirector.cs` | Scrub UI/input; on confirm, `Commit`s the rewind and spawns an echo seeded from the restored state. |
| `Assets/Scripts/Commands/ClonePlayback.cs` | Replays a `CommandTimeline` onto an echo. |

**Choice — replay by *absolute clock tick*, not a spawn‑relative index.** `ClonePlayback` executes the command recorded for the current clock tick (`GetAtTick`). So a clock rewind automatically rewinds the replay for free, and the echo's pose is restored by its own `RigidbodyChannel`. Forward, physics is non‑deterministic so echoes drift — that divergence is the point.

**Choice — clone retires via the rewind seam, not death.** When a replay catches up to where it was spawned, the echo `Despawn`s (deactivate + retain), so scrubbing back into its `[spawn, end]` window revives it — its alive record matches its timeline lane. Echoes are seeded from the Rigidbody (which holds the restored state right after a rewind), and deep spawn overlaps are handled by the controller (§6).

---

## 5. Player movement & physics

| File | Role |
|------|------|
| `Assets/Scripts/Player/PlayerController.cs` | All movement, gravity, ground/wall handling, character coupling. |
| `Assets/Scripts/Player/Player.cs` | Thin command‑receiver facade (`Move`/`Jump`/`SetJumpHeld`/`Use`). |
| `Assets/Scripts/Entities/Entity.cs` | `Kill()` → rewindable `Despawn` (deactivate + retain), else `SetActive(false)`. |

Dynamic body with **custom gravity** (`gravityScale = 0`, gravity applied in code) so jump feel stays tunable; velocity is set every tick and the solver resolves contacts.

- **Choice — momentum conservation above move speed** (`ResolveSpeed`): driving the same way faster than the input target bleeds down only at a gentle rate (0 = full conserve), so speed earned off a fast platform or slope is kept; air always conserves.
- **Coupling / carry:** movement works *relative* to the surface/contact velocity (`baseVelocity`), measured against last tick's base so a carrier's own acceleration isn't absorbed — rigid "snappy carry" rather than friction‑lag. Side‑pushes from bodies pressing into us are inherited horizontally (capped), opposing pushers cancel.
- **Rewind‑safe timing:** jump buffer, coyote time and post‑jump ground‑suppress are stored as absolute tick *stamps* compared to the current tick, so a backward tick invalidates them (no phantom jumps).
- **Deep‑overlap resolution** (`ResolveCharacterOverlaps`): two characters overlapping deeply (a clone spawned/rewound *into* another) ignore their collision until nearly clear, with hysteresis — covers spawn, revive and rewind‑reposition with one rule.
- Frictionless shared material (carry is done by velocity matching, not friction); into‑wall x is cancelled so a frictionless steep slope can't slide us up.

---

## 6. Interactables — generic rewindable `Toggleable` *(redesigned this round)*

| File | Role |
|------|------|
| `Assets/Scripts/Interactables/Toggleable.cs` | Base: `startActive`/`IsActive`/`SetActive`/`Toggle`; toggles child renderers + colliders (lazy‑cached, null‑safe). |
| `…/Interactables/Lever.cs` | `Lever : Toggleable, IInteractable`; holds a `Toggleable[]` target list, cascades `Toggle()`. |
| `…/Interactables/IInteractable.cs` | `Interact()`. |
| `Assets/Scripts/Hazards/Hazard.cs` | `OnTriggerEnter2D → Entity.Kill()`. |
| `Assets/Scripts/Commands/InteractionDetector.cs` | On the player: finds the nearest `IInteractable` in range (`Use` resolves from the body's own position). |
| `Assets/Scripts/Rewind/Channels/ToggleableChannel.cs` | Rewinds any `Toggleable` (sparse). |

**Choice — one generic `Toggleable`, replacing `Door` + per‑scene `UnityEvent` wiring.** The old system had a `Door` and a `Lever` whose `On`/`Off` `UnityEvent`s were wired to each door per scene (a long action list). It was replaced by: a component you drop on *any* prefab with a default state, and a lever that holds **direct references** to its targets.

**Choice — toggle semantics, not absolute set.** A lever **flips** each target (each keeps its own `startActive` default); levers can target other levers (cascade is cycle‑guarded). "Active" = renderers + colliders enabled (present/blocking/harmful); inactive = hidden + passable/safe.

**Choice — the cascade is rewind‑safe by construction.** Every toggleable carries its own `RewindableEntity + ToggleableChannel`. The cascade (`Lever.Toggle → target.Toggle`) runs **only** during live play and clone replay (via `UseCommand`). Rewind *restore* goes through `SetActive`, which applies only the object's own effect (renderers/handle) and **never cascades** — so scrubbing restores every lever/door/spike independently, no double‑driving.

**Choice — doors stay active, toggle renderer/collider** (not `SetActive(false)`): deactivating the GameObject collided with `RewindableEntity`'s own dormancy and produced impossible rewind states.

Prefabs: `barrier_2x1x{2,4}_blue` migrated to `Toggleable + ToggleableChannel`; new `spike_hazard` prefab (real floor‑spike mesh, base + spikes child carrying the `Hazard` + kill‑trigger) registered in the Level Painter *Hazards* palette.

---

## 7. Best‑run shadow

| File | Role |
|------|------|
| `Assets/Scripts/Shadow/BestRunRecorder.cs` | On finish, exports every body's path and saves it if the run beat the stored time. |
| `…/Shadow/ShadowRecord.cs` | `ShadowRecord` (BestTick + tracks) / `ShadowTrack` (spawnTick + positions). |
| `…/Shadow/ShadowStore.cs` | Versioned binary persistence per level under `persistentDataPath/ghosts/<level>.ghost`. |
| `…/Shadow/ShadowDirector.cs` | On level load, spawns one shadow body per saved track. |
| `…/Shadow/ShadowPlayback.cs` | Drives a shadow: a pure position write per clock tick, **no physics**. |

**Choice — reuse the rewind capture.** The shadow path *is* the `RigidbodyChannel`'s dense history, exported at the finish line (`TryExportPositions`) while the player + every clone are still alive — no separate recording system. "Best" = lowest finish `Tick`.

**Choice — position‑only, no‑physics replay** (addressed by absolute tick, like clones): the shadow retraces exactly with zero drift, and a live‑attempt rewind rewinds the shadow for free. Persistence is defensive (corrupt/missing/wrong‑version → "no record", never blocks gameplay). Assets: `ShadowSilhouette` shader + `PlayerShadow` material/prefab.

---

## 8. Levels & flow

| File | Role |
|------|------|
| `Assets/Scripts/Levels/LevelSet.cs` | ScriptableObject: ordered `List<AssetReference>` of scenes. |
| `Assets/Scripts/Levels/LevelLoader.cs` | Loads by index via Addressables; `LoadNext`/`Restart`. |
| `Assets/Scripts/Levels/Level.cs` | Per‑level display name; in‑editor, syncs `LevelLoader.Index` to the scene's slot. |
| `Assets/Scripts/Levels/FinishFlag.cs` | Finish trigger: record best run, then transition. |
| `Assets/Scripts/UI/LevelTransition.cs` | CRT "static/snow" outro between scenes. |

The roster is data‑driven (`LevelSet`, addressable address `levelSet`). `FinishFlag` records the best run **before** unloading (bodies still alive), then routes through `LevelTransition` (snow → load) if present, else loads directly.

**Choice this round — loop the roster.** `LevelLoader.LoadNext` now wraps modulo the set (`(Index+1) % count`), so finishing the last level returns to the first. The `LevelSet` was completed to all nine scenes in order (01→09); previously it skipped 06–08 and ended at 09. Every level carries the finish‑flag prefab, so the loop progresses through each. *(Note: Level 6's two formerly open‑by‑default doors now toggle, so its lever closes them on first pull — flagged for a playtest.)*

---

## 9. Camera

| File | Role |
|------|------|
| `Assets/Scripts/Camera/CameraSpeedDamping.cs` | Speed‑scaled Cinemachine follow damping. |
| `Assets/Scripts/Camera/CinemachineGridConfiner.cs` | Confines the camera to the level grid bounds. |

**Choice — damping eases with the player's own speed.** Relaxed `baseDamping` at/below move speed (smooth on small hops) → tight `fastDamping` by `fastSpeedMultiple × moveSpeed` (keeps up with fast slope descents / conserved momentum). The damping value itself is smoothed so crossing the threshold doesn't pop.

**Choice — lock + unscaled time while scrubbing.** While the timeline is open the clock is paused (`timeScale 0`), which would freeze Cinemachine; the brain is flipped to `IgnoreTimeScale` *only then* and damping locks tight, so the camera keeps tracking the player through a rewind. Normal play is untouched.

---

## 10. Level editor & grid *(authoring tooling)*

| File | Role |
|------|------|
| `Assets/Scripts/LevelGrid/` — `GridObject`, `GridBounds`, `GridLevelLayout`, `LevelPaintMode`, `LevelPalette(+Item)` | Runtime grid model + palette data (a palette = a list of placeable prefabs). |
| `Assets/Editor/Scripts/LevelPainter/` — `LevelPainterWindow`, `LevelPaletteService`, `LevelPainterSession`, `LevelGridEditor`, … | The in‑editor painting window; palettes are discovered as `LevelPalette` assets (`Assets/Settings/Palettes/{Misc,Hazards,Structure}.asset`). |
| `Assets/Editor/Scripts/Migration/PhysicsMigration3DTo2D.cs` | One‑off editor tool from the 3D→2D physics migration. |

Levels are painted from prefab palettes onto a grid; placeables carry a `GridObject` (size/pivot). The spike was added by appending its prefab to the `Hazards` palette asset.

---

## 11. UI / HUD *(CRT theme)*

| File | Role |
|------|------|
| `Assets/Scripts/UI/LevelHud.cs` | HUD root (channel number + level name, transport, timeline). |
| `…/UI/ActionTimeline.cs`, `LaneView.cs` | The scrub timeline with one coloured lane per clone. |
| `…/UI/TransportState.cs` | Play / Pause / Rewind / FastForward indicator. |
| `…/UI/TryTimers.cs` | RT wall‑clock + rewindable TC timecode for the current try. |
| `…/UI/ControlsHint.cs` | Context controls toast (gameplay vs timeline). |

The `RewindDirector` (§4) drives these: each clone gets a lane coloured over its `[spawn, end]` window; the transport reflects scrub direction.

---

## Cross‑cutting design principles

- **One integer clock; absolute‑tick addressing everywhere.** Rewind, clones and shadows all key off the same `Tick`, so "rewind" is a single `Tick` decrement and every consumer follows for free.
- **Capture the entering state, restore‑then‑re‑run.** Snapshots are taken before movers act, so restoring and re‑running a tick is exact.
- **Retain, don't destroy.** Existence is rewindable data (the alive record); objects are reclaimed only once they can never be a target again.
- **Restore is side‑effect‑free.** Channels/levers reapply only their own state on restore; cross‑object effects happen only during live play/replay — the rule that keeps the whole world independently rewindable.
- **Robustness:** time advances even if a tickable throws; input can't latch through an exception; shadow persistence degrades to "no record" rather than crashing.
