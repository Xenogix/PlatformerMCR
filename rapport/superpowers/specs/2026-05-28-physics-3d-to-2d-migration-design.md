# Physics: 3D → 2D Migration Design

**Date:** 2026-05-28
**Branch:** `migrate-physics-3d-to-2d`
**Author/driver:** Claude Code (Opus 4.7) under brainstorming with the user
**Status:** Approved, pending implementation

## Goal

Convert all gameplay-relevant physics in this Unity project from the 3D physics
stack (`BoxCollider`, `MeshCollider`, `Rigidbody`) to the 2D physics stack
(`BoxCollider2D`, `PolygonCollider2D`, `Rigidbody2D`), while keeping the
imported 3D meshes as visuals. The end state is a "2.5D" side-scrolling
platformer: 3D-rendered, 2D-simulated.

## Scope

### In scope

- All 64 prefabs under `Assets/Prefabs/` (Layout + Hazards)
- Scene-instance overrides on `Assets/Scenes/SampleScene.unity`
- A new `Player` GameObject in `SampleScene.unity` (cube primitive + 2D physics
  + existing `PlayerController` stub)
- Project `Physics2D` settings (gravity only)

### Out of scope (explicit non-goals)

- Implementing `PlayerController.Move` / `Jump` bodies — left as stub
- Sprites or 2D-specific visuals; we keep the imported 3D meshes
- Layer collision matrix; ground/player/hazard layers
- Camera rework (an orthographic camera looking down −Z is a natural follow-up
  but not part of this migration)
- Cinemachine, animation, or input rework

## Approach

Migration is driven through **Unity MCP** against the running editor instance
(`PlatformerMCR@0f91e17b…`, Unity 6000.4.6f1). No editor scripts are added to
the repo. Edits use the MCP `manage_components` / `manage_gameobject` /
`manage_prefabs` APIs, which delegate to Unity's serialized-object writes
(undo-safe, validation-aware).

### Axis convention

X+Y is the play plane. Z is dropped from every component field. This is the
standard convention for a side-scroller with an orthographic camera looking
down −Z.

## Component mapping rules

Applied to every prefab under `Assets/Prefabs/`.

### `BoxCollider` → `BoxCollider2D`

For each `BoxCollider` on a GameObject:

- `BoxCollider2D.size = (src.size.x, src.size.y)`
- `BoxCollider2D.offset = (src.center.x, src.center.y)`
- `isTrigger` carried over
- `gameObject.layer` preserved (no layer reassignment in this migration)
- Source `BoxCollider` destroyed last (after the 2D component is in place)

Applies to all 64 prefabs.

### `MeshCollider` → `PolygonCollider2D`

For each `MeshCollider` on a GameObject:

1. Read the source mesh from the sibling `MeshFilter` (or the
   `MeshCollider.sharedMesh` if explicitly set there).
2. Project every vertex onto the XY plane (drop Z).
3. Compute the convex hull of the projected points.
4. Write the hull as `PolygonCollider2D.points` (single path, `pathCount = 1`).
5. `isTrigger` carried over.
6. Source `MeshCollider` destroyed last.

Applies to: the 10 `platform_slope_*` prefabs (each yields a 3-point triangle)
and `platform_hole_6x6x1_blue.prefab`.

### Hole platform — special case

`platform_hole_6x6x1_blue.prefab` is the one prefab where convex hull is wrong
in principle: a "hole" platform is a donut shape (outer rectangle minus an
inner cut-out) that needs **two paths** on the `PolygonCollider2D`
(`pathCount = 2`, path 0 = outer outline, path 1 = inner hole, opposite
winding).

Implementation:

- Inspect the mesh in the editor first
- If the geometry actually has an inner void, author the polygon with both
  paths
- If the geometry is a solid block with only a *visual* hole in the texture or
  mesh-decoration, convex hull is fine — treat as a normal box-ish prefab

This is the one prefab we look at by hand before scripting its swap.

### Carry-over rules

- `isTrigger` is preserved per-collider.
- `gameObject.layer` and `tag` are not modified.
- Multiple `BoxCollider`s on a single GameObject (e.g., a hazard with several
  hitboxes) are each migrated independently.

## Player rig

A new GameObject is created at the root of `SampleScene.unity`:

- **Name:** `Player`
- **Visual:** Unity primitive `Cube` (1×1×1, default material) — created via
  `GameObject.CreatePrimitive(PrimitiveType.Cube)` so the MeshFilter,
  MeshRenderer, and existing 3D `BoxCollider` are present, then the 3D
  `BoxCollider` is removed.
- **Rigidbody2D**:
  - `bodyType = Dynamic`
  - `gravityScale = 4`
  - `freezeRotation = true` (constraints: freeze Z rotation)
  - `interpolation = Interpolate`
  - `collisionDetectionMode = Continuous`
- **BoxCollider2D**:
  - `size = (1, 1)` — square, matching the cube's XY profile
  - `offset = (0, 0)`
- **PlayerController** — the existing stub script (`Assets/Scripts/Player/PlayerController.cs`)
  attached; bodies remain empty.

## Project Physics2D settings

`ProjectSettings/Physics2DSettings.asset`:

- `gravity = (0, -40)` — snappy arcade feel (Celeste-ish). Default `-9.81` is
  too floaty for this genre.
- All other fields left at defaults (sleep thresholds, velocity iterations,
  layer collision matrix).

## Execution order

1. **Sanity check** — read `mcpforunity://editor/state`; assert not in Play
   mode and no pending compile errors.
2. **Physics2D settings** — set gravity = (0, -40).
3. **Migrate all 64 prefabs** in alphabetical order under `Assets/Prefabs/`.
   For each prefab:
   - Log "before" component list
   - Apply box and/or mesh collider swap rules
   - Log "after" component list and confirm zero 3D physics components remain
4. **Migrate `SampleScene.unity`** — for every GameObject in the open scene
   that still carries a 3D physics component (whether on a prefab instance,
   an instance override, or a non-prefab GameObject), apply the same swap
   rules. A prefab-instance object whose prefab was already migrated in
   step 3 should already be clean; we only act on the residue.
5. **Create Player** in `SampleScene.unity` per the rig spec above.
6. **Final verification** — see below.

## Verification

- Editor console clear of errors and warnings after migration.
- Spot-check three prefabs by opening them in isolation:
  - One simple box (e.g., `platform_4x4x1_blue`)
  - One slope (e.g., `platform_slope_4x4x4_blue`)
  - The hole platform (`platform_hole_6x6x1_blue`)
  - Confirm Scene-view 2D collider gizmos match the mesh silhouette
- Press Play briefly: the Player cube should fall under gravity and land on a
  platform. This is proof-of-life, not gameplay validation.

## Rollback

All work is on branch `migrate-physics-3d-to-2d`.

- Mid-migration revert of a single prefab: `git checkout -- <path>`
- Catastrophic abort: `git checkout main && git branch -D migrate-physics-3d-to-2d`

The MCP edits go straight to the working tree, so git is the rollback
mechanism. No automatic backups beyond the branch.

## Risks & open questions

- **Hole platform geometry** — needs visual confirmation before scripting. May
  be a one-off `pathCount = 2` polygon or a plain rect.
- **Mesh orientation** — assumed all slope wedges have their diagonal in the
  XY plane with Z as depth. If any slope FBX is rotated differently, its
  convex hull will look wrong; spot-checks in step 6 will catch this.
- **Hidden Rigidbody references** — none found by grep in `Assets/Scripts/`,
  but if scripts on Hazard prefabs reference `Rigidbody` via `GetComponent`,
  they'll silently fail at runtime. We don't grep for these in this
  migration; flagged for the user to verify after Play-testing.

## Out-of-scope follow-ups (worth noting for later)

- Switch main camera to orthographic, oriented down −Z
- Implement `PlayerController.Move(Vector2)` and `Jump()` bodies
- Define gameplay layers (`Ground`, `Player`, `Hazard`) and configure the
  layer collision matrix
- Migrate scene lighting if it doesn't read well in side-view
