# Best-Run Shadow — Implementation Notes

Persists a level's fastest run (player + every alive clone) as per-tick position tracks on disk,
and on re-entry spawns dark/translucent "shadow" bodies that retrace those exact positions by clock
tick (kinematic, no physics → exact retrace, rewind-aware). "Best" = lowest `GameClock.Tick` at finish.

## Files created
- `Assets/Scripts/Shadow/ShadowRecord.cs` — `ShadowTrack` (one body's path) + `ShadowRecord` (a whole run).
- `Assets/Scripts/Shadow/ShadowStore.cs` — versioned binary save/load, one `.ghost` file per level; defensive reads.
- `Assets/Scripts/Shadow/BestRunRecorder.cs` — `RecordIfBest()`: snapshot player + clones, save iff faster.
- `Assets/Scripts/Shadow/ShadowPlayback.cs` — `MonoBehaviour, ITickable`; pure per-tick position write.
- `Assets/Scripts/Shadow/ShadowDirector.cs` — on level load, spawns one shadow per saved track.

## Files edited
- `Assets/Scripts/Rewind/History/DenseHistory.cs` — added `BaseTick`, `Count`, `ValueAt(i)` read-out.
- `Assets/Scripts/Rewind/Channels/RewindChannel.cs` — added `protected IHistory<T> History => _history;`.
- `Assets/Scripts/Rewind/Channels/RigidbodyChannel.cs` — added `TryExportPositions(...)` (+ `using System.Collections.Generic;`).
- `Assets/Scripts/Levels/FinishFlag.cs` — call `BestRunRecorder.RecordIfBest()` right after `hasTriggered = true;`.

## Remaining manual Unity steps (Editor only — NOT done by this change)
1. **Create `Assets/Prefabs/Game/PlayerShadow.prefab`** — a visual-only shadow body:
   - Easiest: duplicate `Assets/Prefabs/Game/PlayerEcho.prefab`, then REMOVE its `Rigidbody2D`, all
     `Collider2D`, and the `Player` / `PlayerController` / `ClonePlayback` / `PlayerCommandInvoker` /
     `RewindableEntity` / `RigidbodyChannel` scripts. Note `RequireComponent` chains: remove dependent
     components before the ones they require (e.g. `Player` requires `PlayerController`; `RigidbodyChannel`
     requires `Rigidbody2D`). If removal order fights you, just build a fresh GameObject with only the
     player/echo mesh child instead.
   - Assign a dark, more-transparent material to the mesh.
   - Add the **`ShadowPlayback`** component at the prefab root.
   - Keep the prefab transform where you want the shadow's z-plane (z is preserved per tick).
2. **Add a `ShadowDirector` component to each level** (or to the shared Level prefab if there is one) and
   assign the `PlayerShadow` prefab to its `shadowPrefab` field.

## Verification checklist
- [ ] Unity compiles clean (no `Assembly-CSharp` errors in the console).
- [ ] In-editor round-trip: `ShadowStore.Save(key, rec)` then `ShadowStore.TryLoad(key, out var back)`
      reproduces `BestTick` and every track's `SpawnTick` + `Positions` exactly.
- [ ] Finish a level → `<persistentDataPath>/ghosts/<scene>.ghost` appears on disk.
- [ ] Re-enter the level → shadow bodies retrace the player AND each clone that was alive.
- [ ] Beat the time → file is updated (new lower `BestTick`); a slower run leaves the file unchanged.
- [ ] Restart the Editor → the record still loads (persistence survives the session).
- [ ] During a fresh live attempt, rewinding the clock also rewinds the shadows (they are clock-tick addressed).

## Assumptions / risks
- **captureRate == 1 (per-tick):** the export assumes one capture per tick (the project's setting). If
  `captureRate` ever changes, `DenseHistory` step would be > 1 and the exported positions would no longer
  be one-per-tick, so `ShadowPlayback`'s `tick - spawnTick` index would desync.
- **Scene name is the level key:** records key off `SceneManager.GetActiveScene().name`. Renaming a scene
  orphans its `.ghost`; two scenes sharing a name would collide.
- **Shadow z-plane comes from the prefab:** `ShadowPlayback` preserves `transform.position.z`, so the
  prefab's z decides the draw plane relative to the level.
- **Material shader (URP vs built-in):** the transparent shadow material's color property differs by
  pipeline (`_BaseColor` under URP, `_Color` under built-in). Pick the one matching the project's pipeline.
- A corrupt/old/missing `.ghost` is treated as "no record" (load returns false) and save IO errors only
  warn — gameplay is never blocked by shadow persistence.
