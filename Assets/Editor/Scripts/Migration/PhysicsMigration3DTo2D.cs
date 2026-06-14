using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PhysicsMigration3DTo2D
{
    private const string PrefabsRoot = "Assets/Prefabs";
    private const string SamplePlayerName = "Player";
    private const float PlayerGravityScale = 4f;
    private static readonly Vector2 PlayerColliderSize = new(1f, 1f);
    private static readonly Vector3 PlayerSpawn = new(0f, 5f, 0f);
    private static readonly Vector2 WorldGravity = new(0f, -40f);

    [MenuItem("Tools/Migrate 3D Physics → 2D/Run Full Migration")]
    public static void RunAll()
    {
        if (!EditorUtility.DisplayDialog(
                "Migrate Physics 3D → 2D",
                "This rewrites every prefab under Assets/Prefabs/, modifies SampleScene, " +
                "creates a Player GameObject, and sets Physics2D.gravity. " +
                "Make sure you're on a dedicated branch with no unsaved changes elsewhere.\n\n" +
                "Continue?",
                "Run", "Cancel"))
        {
            return;
        }

        Physics2D.gravity = WorldGravity;
        AssetDatabase.SaveAssets();

        var prefabCount = MigratePrefabs();
        var sceneChanged = MigrateActiveScene();
        var playerCreated = CreatePlayerIfMissing();

        if (sceneChanged || playerCreated)
        {
            EditorSceneManager.SaveOpenScenes();
        }

        Debug.Log(
            $"[PhysicsMigration3DTo2D] Done. Prefabs migrated: {prefabCount}. " +
            $"Scene modified: {sceneChanged}. Player created: {playerCreated}.");
    }

    [MenuItem("Tools/Migrate 3D Physics → 2D/Prefabs Only")]
    public static void RunPrefabsOnly()
    {
        var count = MigratePrefabs();
        Debug.Log($"[PhysicsMigration3DTo2D] Prefabs migrated: {count}.");
    }

    private static int MigratePrefabs()
    {
        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabsRoot });
        int touched = 0;

        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (MigrateGameObjectTree(root))
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    touched++;
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        AssetDatabase.SaveAssets();
        return touched;
    }

    private static bool MigrateActiveScene()
    {
        var scene = SceneManager.GetActiveScene();
        bool any = false;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (MigrateGameObjectTree(root))
            {
                any = true;
                EditorUtility.SetDirty(root);
            }
        }
        return any;
    }

    private static bool CreatePlayerIfMissing()
    {
        var existing = GameObject.Find(SamplePlayerName);
        if (existing != null) return false;

        var player = GameObject.CreatePrimitive(PrimitiveType.Cube);
        player.name = SamplePlayerName;
        player.transform.position = PlayerSpawn;

        var box3d = player.GetComponent<BoxCollider>();
        if (box3d != null) Object.DestroyImmediate(box3d);

        var rb = player.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = PlayerGravityScale;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        var bc = player.AddComponent<BoxCollider2D>();
        bc.size = PlayerColliderSize;
        bc.offset = Vector2.zero;

        var controllerType = System.Type.GetType("PlayerController, Assembly-CSharp");
        if (controllerType != null)
        {
            player.AddComponent(controllerType);
        }
        else
        {
            Debug.LogWarning("[PhysicsMigration3DTo2D] PlayerController type not found; player created without it.");
        }

        EditorUtility.SetDirty(player);
        return true;
    }

    private static bool MigrateGameObjectTree(GameObject root)
    {
        bool changed = false;
        var meshColliders = root.GetComponentsInChildren<MeshCollider>(true);
        foreach (var mc in meshColliders)
        {
            ReplaceMeshCollider(mc);
            changed = true;
        }

        var boxColliders = root.GetComponentsInChildren<BoxCollider>(true);
        foreach (var bc in boxColliders)
        {
            ReplaceBoxCollider(bc);
            changed = true;
        }

        return changed;
    }

    private static void ReplaceBoxCollider(BoxCollider source)
    {
        var go = source.gameObject;
        var size = new Vector2(source.size.x, source.size.y);
        var offset = new Vector2(source.center.x, source.center.y);
        var isTrigger = source.isTrigger;

        // Unity refuses to add a Collider2D while a derived 3D Collider is present.
        Object.DestroyImmediate(source);

        var host = ColliderHostFor(go);
        var box2d = host.AddComponent<BoxCollider2D>();
        box2d.size = size;
        box2d.offset = offset;
        box2d.isTrigger = isTrigger;
    }

    private static readonly Regex SlopeNamePattern =
        new(@"slope_(\d+)x(\d+)x\d+", RegexOptions.Compiled);
    private static readonly Regex HoleNamePattern =
        new(@"hole_(\d+)x(\d+)x\d+", RegexOptions.Compiled);

    private static void ReplaceMeshCollider(MeshCollider source)
    {
        var go = source.gameObject;
        var isTrigger = source.isTrigger;

        // Polygon shape comes from the prefab name (slope → trapezoid that
        // collapses to a triangle when right-height is 0; hole → rectangle); the
        // dimensions and silhouette come from the mesh, projected to world XY
        // and folded back into the host-child's local frame.
        var mesh = source.sharedMesh ?? go.GetComponent<MeshFilter>()?.sharedMesh;
        var meshData = mesh != null ? ComputeMeshSilhouetteInGoLocal(go, mesh) : (MeshSilhouette?)null;
        var points = BuildPolygonForName(go, meshData);

        if (points == null)
        {
            Debug.LogWarning($"[PhysicsMigration3DTo2D] {go.name} has MeshCollider but no name match; leaving an empty PolygonCollider2D — please author by hand.");
        }

        // Unity refuses to add a Collider2D while a derived 3D Collider is present.
        Object.DestroyImmediate(source);

        var host = ColliderHostFor(go);
        var poly = host.AddComponent<PolygonCollider2D>();
        poly.isTrigger = isTrigger;
        if (points != null)
        {
            poly.pathCount = 1;
            poly.SetPath(0, points);
        }
    }

    // Mesh silhouette projected into the GameObject's *position-translated, rotation-
    // honoured* frame — i.e., the frame the collider host child sits in (child has
    // identity world rotation but inherits the parent's world position). Returns the
    // AABB plus the max Y observed near the left (xmin) and right (xmax) extents,
    // used to size the slope trapezoid correctly when the silhouette isn't a clean
    // triangle (e.g., when there's a lip at the bottom-right).
    private struct MeshSilhouette
    {
        public Rect Aabb;
        public float LeftHeight;
        public float RightHeight;
    }

    private const float ExtentEpsilon = 0.1f;

    private static MeshSilhouette ComputeMeshSilhouetteInGoLocal(GameObject go, Mesh mesh)
    {
        var ltw = go.transform.localToWorldMatrix;
        var goWorld = (Vector2)go.transform.position;

        float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
        var pts = new Vector2[mesh.vertexCount];
        var verts = mesh.vertices;
        for (int i = 0; i < verts.Length; i++)
        {
            var w = ltw.MultiplyPoint3x4(verts[i]);
            var p = new Vector2(w.x - goWorld.x, w.y - goWorld.y);
            pts[i] = p;
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
        }

        float leftHeight = float.NegativeInfinity, rightHeight = float.NegativeInfinity;
        foreach (var p in pts)
        {
            if (p.x <= minX + ExtentEpsilon && p.y > leftHeight) leftHeight = p.y;
            if (p.x >= maxX - ExtentEpsilon && p.y > rightHeight) rightHeight = p.y;
        }

        return new MeshSilhouette
        {
            Aabb = new Rect(minX, minY, maxX - minX, maxY - minY),
            LeftHeight = leftHeight,
            RightHeight = rightHeight,
        };
    }

    private const string ColliderChildName = "Collider2D";

    // 2D physics colliders honour the full 3D transform of their GameObject — an X/Y
    // rotation rotates the collider out of the XY plane and breaks collision.
    // For any GameObject with a non-Z rotation, host the 2D collider on a child whose
    // local rotation cancels the parent's, so the child's world rotation is identity
    // (or a pure Z rotation) and the polygon stays in world XY.
    private static GameObject ColliderHostFor(GameObject go)
    {
        if (!HasOutOfPlaneRotation(go.transform)) return go;

        var existing = go.transform.Find(ColliderChildName);
        if (existing != null) return existing.gameObject;

        var child = new GameObject(ColliderChildName);
        child.transform.SetParent(go.transform, false);
        child.transform.localRotation = Quaternion.Inverse(go.transform.localRotation);
        return child;
    }

    private static bool HasOutOfPlaneRotation(Transform t)
    {
        var e = t.localEulerAngles;
        var x = Mathf.DeltaAngle(0f, e.x);
        var y = Mathf.DeltaAngle(0f, e.y);
        return Mathf.Abs(x) > 0.01f || Mathf.Abs(y) > 0.01f;
    }

    // Picks the polygon SHAPE based on the prefab name (slope → trapezoid sized
    // by the mesh's left/right heights; hole → AABB rectangle) and uses the mesh
    // silhouette for the actual coordinates. If the mesh is unavailable, falls
    // back to the WxH from the name (a perfect triangle for slopes — only correct
    // for prefabs with identity rotation, which the 9 slopes are NOT).
    private static Vector2[] BuildPolygonForName(GameObject go, MeshSilhouette? meshData)
    {
        foreach (var candidate in NamesToTry(go))
        {
            if (SlopeNamePattern.IsMatch(candidate))
            {
                if (meshData.HasValue)
                {
                    var s = meshData.Value;
                    // Trapezoid: bottom edge + right vertical lip + sloped top + left vertical
                    // edge. When rightHeight ≈ yMin the trapezoid degenerates cleanly to a
                    // triangle; PolygonCollider2D handles the duplicate corner.
                    return new[]
                    {
                        new Vector2(s.Aabb.xMin, s.Aabb.yMin),
                        new Vector2(s.Aabb.xMax, s.Aabb.yMin),
                        new Vector2(s.Aabb.xMax, s.RightHeight),
                        new Vector2(s.Aabb.xMin, s.LeftHeight),
                    };
                }
                var fallback = FallbackAabbFromName(candidate);
                return new[]
                {
                    new Vector2(fallback.xMin, fallback.yMin),
                    new Vector2(fallback.xMax, fallback.yMin),
                    new Vector2(fallback.xMin, fallback.yMax),
                };
            }

            if (HoleNamePattern.IsMatch(candidate))
            {
                var box = meshData?.Aabb ?? FallbackAabbFromName(candidate);
                return new[]
                {
                    new Vector2(box.xMin, box.yMin),
                    new Vector2(box.xMax, box.yMin),
                    new Vector2(box.xMax, box.yMax),
                    new Vector2(box.xMin, box.yMax),
                };
            }
        }
        return null;
    }

    private static Rect FallbackAabbFromName(string name)
    {
        var m = SlopeNamePattern.Match(name);
        if (!m.Success) m = HoleNamePattern.Match(name);
        float w = int.Parse(m.Groups[1].Value);
        float h = int.Parse(m.Groups[2].Value);
        return new Rect(-w / 2f, 0f, w, h);
    }

    private static IEnumerable<string> NamesToTry(GameObject go)
    {
        yield return go.name;
        var root = go.transform.root != null ? go.transform.root.gameObject : null;
        if (root != null && root != go) yield return root.name;
    }

    // Andrew's monotone chain convex hull. O(n log n).
    private static Vector2[] ConvexHull(List<Vector2> points)
    {
        if (points.Count < 3) return points.ToArray();

        points = points.OrderBy(p => p.x).ThenBy(p => p.y).ToList();
        var lower = new List<Vector2>();
        var upper = new List<Vector2>();

        foreach (var p in points)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], p) <= 0)
                lower.RemoveAt(lower.Count - 1);
            lower.Add(p);
        }

        for (int i = points.Count - 1; i >= 0; i--)
        {
            var p = points[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], p) <= 0)
                upper.RemoveAt(upper.Count - 1);
            upper.Add(p);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        return lower.Concat(upper).ToArray();
    }

    private static float Cross(Vector2 o, Vector2 a, Vector2 b)
        => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
}
