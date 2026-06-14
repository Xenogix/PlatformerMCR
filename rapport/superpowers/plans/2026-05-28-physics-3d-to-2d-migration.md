# Physics 3D → 2D Migration — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate all 3D physics in this Unity project to 2D physics, driving every change through Unity MCP against the running editor — no editor scripts in the repo.

**Architecture:** Approach **B** from the brainstorm. The repo gains zero net code from the migration itself; all edits are MCP `manage_components` / `manage_gameobject` / `execute_code` calls against the live editor (`PlatformerMCR@0f91e17b…`, Unity 6000.4.6f1). 3D meshes are kept as visuals; only colliders/rigidbody types change. XY play plane, Z dropped. See `docs/superpowers/specs/2026-05-28-physics-3d-to-2d-migration-design.md` for full design.

**Tech Stack:** Unity 6000.4.6f1, Unity MCP server, `Rigidbody2D` / `BoxCollider2D` / `PolygonCollider2D`, `ProjectSettings/Physics2DSettings.asset`.

---

## Files touched

- **Modify:** all 64 prefabs under `Assets/Prefabs/Layout/` and `Assets/Prefabs/Hazards/`
- **Modify:** `Assets/Scenes/SampleScene.unity`
- **Modify:** `ProjectSettings/Physics2DSettings.asset`
- **Create:** none (no editor scripts; approach B)

## Working branch

All work commits to `migrate-physics-3d-to-2d` (already created off `main`). PR opens at the end.

---

### Task 0: Pre-flight sanity check

**Files:** none modified.

- [ ] **Step 1: Confirm Unity MCP instance is connected**

Call:
```
ReadMcpResourceTool(server="UnityMCP", uri="mcpforunity://instances")
```
Expected: `instance_count: 1`, `name: "PlatformerMCR"`. If absent, stop and ask the user to open the project in Unity.

- [ ] **Step 2: Confirm editor not in Play mode and no compile errors**

Call:
```
ReadMcpResourceTool(server="UnityMCP", uri="mcpforunity://editor/state")
```
Expected: `isPlaying: false`, `isCompiling: false`. If `isCompiling: true`, wait and re-check.

- [ ] **Step 3: Clear the console for a clean baseline**

Call:
```
mcp__UnityMCP__read_console(action="clear")
```
Expected: success.

- [ ] **Step 4: Confirm current git branch is `migrate-physics-3d-to-2d`**

```bash
git branch --show-current
```
Expected output: `migrate-physics-3d-to-2d`.

---

### Task 1: Set Physics2D gravity

**Files:**
- Modify: `ProjectSettings/Physics2DSettings.asset`

- [ ] **Step 1: Set gravity to (0, -40)**

Use `mcp__UnityMCP__execute_code` to run:
```csharp
UnityEngine.Physics2D.gravity = new UnityEngine.Vector2(0f, -40f);
UnityEditor.AssetDatabase.SaveAssets();
return UnityEngine.Physics2D.gravity.ToString();
```
Expected return: `(0.00, -40.00)`.

- [ ] **Step 2: Verify the asset file was written**

```bash
grep -A1 "m_Gravity" ProjectSettings/Physics2DSettings.asset
```
Expected: a line showing `y: -40`.

- [ ] **Step 3: Commit**

```bash
git add ProjectSettings/Physics2DSettings.asset
git commit -m "$(cat <<'EOF'
chore(physics): set Physics2D gravity to (0, -40)

Snappy-arcade starter value. Default -9.81 was too floaty for a side-scroller.
Tunable later when PlayerController.Move/Jump bodies are implemented.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```
Expected: 1 file changed.

---

### Task 2: Prove the BoxCollider → BoxCollider2D swap on ONE prefab

This is the "make-it-work-once" task. Pick the simplest prefab, do the swap by hand via MCP, verify, commit. If anything goes wrong, we catch it before fanning out to 53 more.

**Files:**
- Modify: `Assets/Prefabs/Layout/platform_1x1x1_blue.prefab`

- [ ] **Step 1: Read the prefab's current components**

Use `mcp__UnityMCP__manage_prefabs` action=`open` on `Assets/Prefabs/Layout/platform_1x1x1_blue.prefab`, then `mcp__UnityMCP__find_gameobjects` + the gameobject components resource to confirm the prefab root has a `BoxCollider` with `size` and `center`.

Record those values — call them `srcSize` and `srcCenter`.

- [ ] **Step 2: Add a BoxCollider2D with mapped size/offset**

`mcp__UnityMCP__manage_components` action=`add` to the prefab root:
- componentType: `BoxCollider2D`
- properties:
  - `size`: `(srcSize.x, srcSize.y)`
  - `offset`: `(srcCenter.x, srcCenter.y)`
  - `isTrigger`: same as source

- [ ] **Step 3: Remove the original BoxCollider**

`mcp__UnityMCP__manage_components` action=`remove`, componentType=`BoxCollider`.

- [ ] **Step 4: Save and close the prefab**

`mcp__UnityMCP__manage_prefabs` action=`save`, then action=`close`.

- [ ] **Step 5: Verify only 2D components remain**

```bash
grep -E "^(BoxCollider|MeshCollider|Rigidbody|CharacterController):" Assets/Prefabs/Layout/platform_1x1x1_blue.prefab
```
Expected: empty output. (3D physics types should be gone.)

```bash
grep -E "^BoxCollider2D:" Assets/Prefabs/Layout/platform_1x1x1_blue.prefab
```
Expected: at least one match.

- [ ] **Step 6: Check console for errors**

```
mcp__UnityMCP__read_console(action="get_messages", types=["error","warning"])
```
Expected: no new errors. (Warnings about the renderer-only prefab are OK.)

- [ ] **Step 7: Commit**

```bash
git add Assets/Prefabs/Layout/platform_1x1x1_blue.prefab
git commit -m "$(cat <<'EOF'
refactor(prefab): migrate platform_1x1x1_blue to BoxCollider2D

Proof-of-life for the BoxCollider → BoxCollider2D swap mechanic before
fanning out to the remaining 53 box-only prefabs.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Batch-migrate the remaining 53 box-only prefabs

Same swap as Task 2, applied to every remaining prefab that has a 3D `BoxCollider` and **no** `MeshCollider`. The 10 prefabs with `MeshCollider` (9 slopes + 1 hole) are excluded here and handled in Tasks 4–5.

**Files:**
- Modify: every prefab under `Assets/Prefabs/Layout/` and `Assets/Prefabs/Hazards/` except the 10 mesh-collider prefabs and the one already done in Task 2.

- [ ] **Step 1: Compute the exact prefab list to migrate**

```bash
comm -23 \
  <(find Assets/Prefabs -name "*.prefab" | sort) \
  <(grep -lE "^MeshCollider:" Assets/Prefabs/Layout/*.prefab Assets/Prefabs/Hazards/*.prefab 2>/dev/null | sort) \
  | grep -v "platform_1x1x1_blue.prefab"
```
Expected: 53 file paths printed. Save the list to a shell variable or scratch file.

- [ ] **Step 2: For each prefab, perform the same swap as Task 2 (steps 1–4)**

Loop over the 53 paths. For each:
1. `manage_prefabs action=open`
2. Read source `BoxCollider` `size`+`center` (and `isTrigger`)
3. Add `BoxCollider2D` with mapped values
4. Remove `BoxCollider`
5. `manage_prefabs action=save` and `action=close`

If a prefab has multiple `BoxCollider`s, migrate each one (preserve order).

- [ ] **Step 3: Bulk verification — no 3D physics components remain in the 53 prefabs**

```bash
grep -lE "^(BoxCollider|MeshCollider|Rigidbody|CharacterController):" $(find Assets/Prefabs -name "*.prefab") | grep -vE "(slope|hole)"
```
Expected: empty output.

```bash
grep -lE "^BoxCollider2D:" $(find Assets/Prefabs -name "*.prefab") | wc -l
```
Expected: 54 (the 53 + Task-2 prefab).

- [ ] **Step 4: Check console for errors**

```
mcp__UnityMCP__read_console(action="get_messages", types=["error"])
```
Expected: no new errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Prefabs/
git commit -m "$(cat <<'EOF'
refactor(prefabs): migrate 53 box-only prefabs to BoxCollider2D

Drops Z from each BoxCollider's size/center. isTrigger carried over.
9 slopes + 1 hole platform still on MeshCollider — handled separately.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Migrate the 9 slope prefabs (Box + Mesh → BoxCollider2D + PolygonCollider2D)

Each slope has both a 3D `BoxCollider` (the prefab's overall bounds) **and** a 3D `MeshCollider` (the wedge surface). Both get replaced. The wedge becomes a triangle via convex hull of the source mesh's projected vertices.

**Files:**
- Modify (9 files):
  - `Assets/Prefabs/Layout/platform_slope_2x2x2_blue.prefab`
  - `Assets/Prefabs/Layout/platform_slope_2x4x4_blue.prefab`
  - `Assets/Prefabs/Layout/platform_slope_2x6x4_blue.prefab`
  - `Assets/Prefabs/Layout/platform_slope_4x2x2_blue.prefab`
  - `Assets/Prefabs/Layout/platform_slope_4x4x4_blue.prefab`
  - `Assets/Prefabs/Layout/platform_slope_4x6x4_blue.prefab`
  - `Assets/Prefabs/Layout/platform_slope_6x2x2_blue.prefab`
  - `Assets/Prefabs/Layout/platform_slope_6x4x4_blue.prefab`
  - `Assets/Prefabs/Layout/platform_slope_6x6x4_blue.prefab`

- [ ] **Step 1: Prove the slope swap on ONE prefab first**

Pick `platform_slope_2x2x2_blue.prefab` (smallest). Open it via `manage_prefabs action=open`.

Compute the wedge polygon via `mcp__UnityMCP__execute_code`:
```csharp
using UnityEngine;
using UnityEditor.Experimental.SceneManagement;
using System.Linq;
using System.Collections.Generic;

var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
var root = stage.prefabContentsRoot;

var mc = root.GetComponentInChildren<MeshCollider>();
var mesh = mc.sharedMesh;
var ltw = mc.transform.localToWorldMatrix;
var rootInv = root.transform.worldToLocalMatrix;

// Project vertices into the prefab root's local XY.
var pts = mesh.vertices
    .Select(v => rootInv.MultiplyPoint3x4(ltw.MultiplyPoint3x4(v)))
    .Select(v => new Vector2(v.x, v.y))
    .Distinct()
    .ToList();

// Convex hull (Andrew's monotone chain).
pts = pts.OrderBy(p => p.x).ThenBy(p => p.y).ToList();
List<Vector2> lower = new(), upper = new();
foreach (var p in pts) {
    while (lower.Count >= 2 && Cross(lower[^2], lower[^1], p) <= 0) lower.RemoveAt(lower.Count - 1);
    lower.Add(p);
}
for (int i = pts.Count - 1; i >= 0; i--) {
    var p = pts[i];
    while (upper.Count >= 2 && Cross(upper[^2], upper[^1], p) <= 0) upper.RemoveAt(upper.Count - 1);
    upper.Add(p);
}
lower.RemoveAt(lower.Count - 1); upper.RemoveAt(upper.Count - 1);
var hull = lower.Concat(upper).ToArray();
return string.Join("|", hull.Select(p => $"{p.x:F3},{p.y:F3}"));

static float Cross(Vector2 O, Vector2 A, Vector2 B) =>
    (A.x - O.x) * (B.y - O.y) - (A.y - O.y) * (B.x - O.x);
```

Expected return: 3 points (a triangle) for a wedge slope, e.g. `-1.000,-1.000|1.000,-1.000|1.000,1.000`.

If the return has more than ~4 points the wedge mesh isn't a clean prism — note the actual point count and proceed; the polygon is still correct, just denser.

- [ ] **Step 2: Apply the polygon to a new PolygonCollider2D, drop the MeshCollider, migrate the Box**

`mcp__UnityMCP__execute_code`:
```csharp
using UnityEngine;
using UnityEditor.SceneManagement;

var stage = PrefabStageUtility.GetCurrentPrefabStage();
var root = stage.prefabContentsRoot;

var mc = root.GetComponentInChildren<MeshCollider>();
var owner = mc.gameObject;
var hull = /* paste the 3 Vector2 points returned by Step 1 */;

var poly = owner.AddComponent<PolygonCollider2D>();
poly.pathCount = 1;
poly.SetPath(0, hull);
poly.isTrigger = mc.isTrigger;
Object.DestroyImmediate(mc);

// Migrate Box too.
var bc = root.GetComponentInChildren<BoxCollider>();
if (bc != null) {
    var b2 = bc.gameObject.AddComponent<BoxCollider2D>();
    b2.size = new Vector2(bc.size.x, bc.size.y);
    b2.offset = new Vector2(bc.center.x, bc.center.y);
    b2.isTrigger = bc.isTrigger;
    Object.DestroyImmediate(bc);
}

return "ok";
```

- [ ] **Step 3: Save and close the prefab**

`mcp__UnityMCP__manage_prefabs` action=`save`, then `close`.

- [ ] **Step 4: Verify the prefab has only 2D physics components**

```bash
grep -E "^(BoxCollider|MeshCollider|Rigidbody|CharacterController):" Assets/Prefabs/Layout/platform_slope_2x2x2_blue.prefab
```
Expected: empty.

```bash
grep -cE "^(BoxCollider2D|PolygonCollider2D):" Assets/Prefabs/Layout/platform_slope_2x2x2_blue.prefab
```
Expected: 2 (one of each).

- [ ] **Step 5: Spot-check in the editor**

Open the prefab in isolation in Unity. Confirm the green 2D collider gizmos trace the wedge silhouette (slope diagonal visible). If the gizmo looks wrong (e.g., square not triangle), stop and re-inspect Step 1's output.

- [ ] **Step 6: Commit**

```bash
git add Assets/Prefabs/Layout/platform_slope_2x2x2_blue.prefab
git commit -m "$(cat <<'EOF'
refactor(prefab): migrate slope_2x2x2_blue to PolygonCollider2D

Proof-of-life for MeshCollider → PolygonCollider2D via convex hull
of projected mesh vertices. Used to validate the technique before
applying to the remaining 8 slopes.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 7: Apply the same technique to the other 8 slope prefabs**

Loop over the remaining 8 slope paths. For each, repeat Steps 1–3 of this task (compute hull → apply hull + Box swap → save+close).

- [ ] **Step 8: Bulk verify the 9 slopes**

```bash
for f in Assets/Prefabs/Layout/platform_slope_*.prefab; do
  if grep -qE "^(MeshCollider|BoxCollider):" "$f"; then echo "FAIL: $f still has 3D physics"; fi
done
echo "done"
```
Expected: only `done` printed.

- [ ] **Step 9: Commit the remaining 8**

```bash
git add Assets/Prefabs/Layout/platform_slope_*.prefab
git commit -m "$(cat <<'EOF'
refactor(prefabs): migrate remaining 8 slope prefabs to PolygonCollider2D

Same convex-hull-of-mesh-vertices technique as the
proof-of-life commit, applied to every platform_slope_*.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Migrate `platform_hole_6x6x1_blue` (special case)

**Files:**
- Modify: `Assets/Prefabs/Layout/platform_hole_6x6x1_blue.prefab`

- [ ] **Step 1: Inspect the mesh in the editor to determine whether the hole is real geometry or just visual**

Open the prefab. In the Scene view, look at the MeshCollider's mesh. Is there an actual void in the middle (donut topology), or is it a solid block with a textured/visual hole?

- If **solid block (visual-only hole):** treat as a normal Box-only prefab — same swap as Task 2 (the Box + Mesh both go, replaced by a single `BoxCollider2D` matching the prefab's overall size). Skip to Step 3.
- If **real donut geometry:** continue to Step 2.

- [ ] **Step 2 (donut case only): Author a two-path PolygonCollider2D**

`mcp__UnityMCP__execute_code`:
```csharp
using UnityEngine;
using UnityEditor.SceneManagement;
using System.Linq;

var stage = PrefabStageUtility.GetCurrentPrefabStage();
var root = stage.prefabContentsRoot;
var mc = root.GetComponentInChildren<MeshCollider>();
var ltw = mc.transform.localToWorldMatrix;
var rootInv = root.transform.worldToLocalMatrix;

// Project to XY in prefab-root local space.
var pts2d = mc.sharedMesh.vertices
    .Select(v => rootInv.MultiplyPoint3x4(ltw.MultiplyPoint3x4(v)))
    .Select(v => new Vector2(v.x, v.y))
    .Distinct()
    .ToArray();

// Outer rectangle from AABB.
float minX = pts2d.Min(p => p.x), maxX = pts2d.Max(p => p.x);
float minY = pts2d.Min(p => p.y), maxY = pts2d.Max(p => p.y);
var outer = new[] {
    new Vector2(minX, minY), new Vector2(maxX, minY),
    new Vector2(maxX, maxY), new Vector2(minX, maxY),
};

// Inner hole: cluster of interior verts. Eyeball it for now or
// hard-code from inspection (e.g., a 2x2 hole centered at origin).
// Replace with the actual extents seen in the editor:
var inner = new[] {
    new Vector2(-1f,-1f), new Vector2(-1f, 1f),
    new Vector2( 1f, 1f), new Vector2( 1f,-1f),
};

var poly = mc.gameObject.AddComponent<PolygonCollider2D>();
poly.pathCount = 2;
poly.SetPath(0, outer);
poly.SetPath(1, inner);
poly.isTrigger = mc.isTrigger;
Object.DestroyImmediate(mc);

// Migrate the Box too (Task 2 swap).
var bc = root.GetComponentInChildren<BoxCollider>();
if (bc != null) {
    var b2 = bc.gameObject.AddComponent<BoxCollider2D>();
    b2.size = new Vector2(bc.size.x, bc.size.y);
    b2.offset = new Vector2(bc.center.x, bc.center.y);
    b2.isTrigger = bc.isTrigger;
    Object.DestroyImmediate(bc);
}
return "ok";
```

Note: the `inner` extents above are placeholders. Before running, eyeball the actual hole dimensions in the editor and substitute them.

- [ ] **Step 3 (solid-block case only): apply the Task 2 swap**

Same as Task 2 steps 2–4 — add `BoxCollider2D`, drop `BoxCollider`, drop `MeshCollider`. The Mesh just goes; no `PolygonCollider2D` is added.

- [ ] **Step 4: Save and close, then verify**

```bash
grep -E "^(MeshCollider|BoxCollider|Rigidbody):" Assets/Prefabs/Layout/platform_hole_6x6x1_blue.prefab
```
Expected: empty.

- [ ] **Step 5: Spot-check in the editor**

Open the prefab. Confirm 2D collider gizmos match the visual hole (or simple rect, depending on Step 1 decision).

- [ ] **Step 6: Commit**

```bash
git add Assets/Prefabs/Layout/platform_hole_6x6x1_blue.prefab
git commit -m "$(cat <<'EOF'
refactor(prefab): migrate platform_hole_6x6x1_blue to 2D physics

[Adjust message based on the path taken in Step 1: either
'BoxCollider2D-only (hole is visual)' or 'two-path
PolygonCollider2D matching donut geometry'.]

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Migrate `SampleScene.unity`

**Files:**
- Modify: `Assets/Scenes/SampleScene.unity`

- [ ] **Step 1: Open the scene**

`mcp__UnityMCP__manage_scene` action=`load`, path=`Assets/Scenes/SampleScene.unity`.

- [ ] **Step 2: Find every scene GameObject that still has a 3D physics component**

`mcp__UnityMCP__execute_code`:
```csharp
using UnityEngine;
using System.Linq;

var hits = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None)
    .Concat<Component>(Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
    .Concat(Object.FindObjectsByType<CharacterController>(FindObjectsSortMode.None))
    .Select(c => $"{c.gameObject.name}:{c.GetType().Name}")
    .ToList();
return hits.Count == 0 ? "clean" : string.Join("\n", hits);
```

Expected: `clean` if Tasks 2–5 cleaned every prefab and no scene-only objects exist with 3D physics. Otherwise a list of GameObject:ComponentType strings.

- [ ] **Step 3: If hits exist, migrate them with the same swap rules**

For each entry in the list, apply Task 2's swap (Box) or Task 4's swap (Mesh). Use `manage_components` against the scene GameObject (not a prefab).

If the list contains a component you didn't expect (e.g., `SphereCollider`), stop and flag to the user — design didn't cover it.

- [ ] **Step 4: Save the scene**

`mcp__UnityMCP__manage_scene` action=`save`.

- [ ] **Step 5: Re-verify the scene is clean**

Re-run the Step 2 query. Expected: `clean`.

- [ ] **Step 6: Console check**

```
mcp__UnityMCP__read_console(action="get_messages", types=["error"])
```
Expected: no new errors.

- [ ] **Step 7: Commit (only if scene was modified)**

```bash
if ! git diff --quiet Assets/Scenes/SampleScene.unity; then
  git add Assets/Scenes/SampleScene.unity
  git commit -m "$(cat <<'EOF'
refactor(scene): migrate SampleScene 3D physics residue to 2D

Cleans any scene-instance overrides or non-prefab GameObjects that
still referenced the 3D physics stack after the prefab migration.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
fi
```

If `git diff` was already empty, skip the commit.

---

### Task 7: Create the Player rig in `SampleScene.unity`

**Files:**
- Modify: `Assets/Scenes/SampleScene.unity`

- [ ] **Step 1: Confirm scene is loaded and no Player exists**

`mcp__UnityMCP__find_gameobjects` search_term=`Player`, search_method=`by_name`.
Expected: `totalCount: 0`.

- [ ] **Step 2: Create the cube primitive and configure it**

`mcp__UnityMCP__execute_code`:
```csharp
using UnityEngine;

var player = GameObject.CreatePrimitive(PrimitiveType.Cube);
player.name = "Player";
player.transform.position = new Vector3(0f, 5f, 0f);  // 5 units above origin so it falls

// Remove the 3D BoxCollider that CreatePrimitive added.
Object.DestroyImmediate(player.GetComponent<BoxCollider>());

// 2D rig.
var rb = player.AddComponent<Rigidbody2D>();
rb.bodyType = RigidbodyType2D.Dynamic;
rb.gravityScale = 4f;
rb.freezeRotation = true;
rb.interpolation = RigidbodyInterpolation2D.Interpolate;
rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

var bc = player.AddComponent<BoxCollider2D>();
bc.size = new Vector2(1f, 1f);
bc.offset = Vector2.zero;

// Attach the existing stub PlayerController script.
var t = System.Type.GetType("PlayerController, Assembly-CSharp");
if (t == null) return "ERROR: PlayerController type not found";
player.AddComponent(t);

return $"Player created at {player.transform.position}";
```
Expected return: `Player created at (0.00, 5.00, 0.00)`.

If the `PlayerController` type isn't found, stop and confirm the script compiles (`mcp__UnityMCP__read_console`).

- [ ] **Step 3: Save the scene**

`mcp__UnityMCP__manage_scene` action=`save`.

- [ ] **Step 4: Verify Player components**

`mcp__UnityMCP__find_gameobjects` search_term=`Player`, search_method=`by_name`. Then read `mcpforunity://scene/gameobject/{id}/components`.
Expected components: `Transform`, `MeshFilter`, `MeshRenderer`, `Rigidbody2D`, `BoxCollider2D`, `PlayerController`. **Not** present: `BoxCollider` (the 3D one).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scenes/SampleScene.unity
git commit -m "$(cat <<'EOF'
feat(player): add Player cube with Rigidbody2D + BoxCollider2D

Cube primitive (1x1x1) as visual; square BoxCollider2D (1,1) to match.
Dynamic Rigidbody2D, gravityScale=4, freezeRotation, continuous CD.
PlayerController stub attached. Bodies of Move/Jump still empty.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 8: Final verification

**Files:** none modified.

- [ ] **Step 1: Repo-wide 3D physics scan**

```bash
grep -rlE "^(BoxCollider|MeshCollider|SphereCollider|CapsuleCollider|Rigidbody|CharacterController):" Assets/Prefabs Assets/Scenes
```
Expected: empty output. (If anything matches, that file still has 3D physics — debug before continuing.)

- [ ] **Step 2: Console clean**

```
mcp__UnityMCP__read_console(action="get_messages", types=["error","warning"])
```
Expected: no errors. Existing pre-migration warnings are OK; new warnings should be inspected.

- [ ] **Step 3: Play mode smoke test**

`mcp__UnityMCP__manage_editor` action=`enter_play_mode`. Wait 2 seconds. Read `mcpforunity://editor/state`, confirm `isPlaying: true`.

Use `mcp__UnityMCP__find_gameobjects` search_term=`Player`, then check the Player's transform Y position via the gameobject resource. Expected: Y has decreased from 5 (falling under gravity) or stabilized on a platform (landed).

`mcp__UnityMCP__manage_editor` action=`exit_play_mode`.

- [ ] **Step 4: If the Player passed through every platform, debug**

Likely causes:
- Scene has no platform under (0,5) → no surprise the cube fell forever. Add one for the smoke test, retry.
- BoxCollider2D `size` is 0 → check Player components.
- Rigidbody2D `bodyType` isn't Dynamic.

Fix root cause, re-run Step 3.

- [ ] **Step 5: Final commit (only if anything was tweaked during Step 4)**

If no tweaks were needed, skip.

---

### Task 9: Open the pull request

**Files:** none modified.

- [ ] **Step 1: Push the branch**

```bash
git push -u origin migrate-physics-3d-to-2d
```

- [ ] **Step 2: Open the PR**

```bash
gh pr create --title "Migrate physics: 3D → 2D" --body "$(cat <<'EOF'
## Summary
- Migrated all 64 prefabs under `Assets/Prefabs/` from 3D physics (`BoxCollider`/`MeshCollider`) to 2D physics (`BoxCollider2D`/`PolygonCollider2D`)
- Added a `Player` cube to `SampleScene.unity` with `Rigidbody2D` + `BoxCollider2D` + the existing `PlayerController` stub
- Set `Physics2D.gravity = (0, -40)` for a snappy-arcade starter feel
- 3D meshes kept as visuals (2.5D — 3D rendered, 2D simulated)

Approach B (MCP-driven, no editor scripts in the repo).

## Test plan
- [ ] Open the project in Unity, confirm no compile errors
- [ ] Open `SampleScene`, press Play — Player cube falls under gravity
- [ ] Spot-check `platform_slope_4x4x4_blue` in isolation: green 2D collider gizmo traces the wedge diagonal
- [ ] Spot-check `platform_hole_6x6x1_blue` in isolation: collider shape matches the hole geometry
- [ ] `grep -rE "^(BoxCollider|MeshCollider|Rigidbody|CharacterController):" Assets/Prefabs Assets/Scenes` returns no matches

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```
Expected: PR URL printed. Return that URL.

---

## Self-review (run after writing)

- **Spec coverage:** Tasks 1–7 cover every "In scope" item from the spec (Physics2D gravity, all prefabs, scene, Player). Tasks 8–9 cover verification and PR. ✅
- **Placeholders:** Step 2 of Task 5 (donut hole inner extents) is intentionally parameterized on visual inspection — flagged inline, not a placeholder failure. No other TBDs/TODOs.
- **Type consistency:** `Rigidbody2D`, `BoxCollider2D`, `PolygonCollider2D` used consistently. `gravityScale=4` and gravity `(0,-40)` consistent between tasks.
- **Granularity:** Tasks 2 and 4 each "prove on one, then batch" so a failure stops early instead of fanning out to 53 broken prefabs.
