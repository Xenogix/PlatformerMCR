using System;
using UnityEngine;

public class GridLayout : MonoBehaviour
{
    // Inspector fields
    public Vector2Int Size = new(10, 10);
    public float CellSize = 1f;

    // Event called when field changed in the editor
    public event Action FieldChanged;

    // Undo action names
    private const string UndoPaintCell = "Paint Level Cell";
    private const string UndoPaintFree = "Paint Level Free";
    private const string UndoReplace = "Replace Level Object";

    private void OnValidate() => FieldChanged?.Invoke();

    /// <summary>
    /// Places <paramref name="prefab"/> at the given grid cell, optionally flipping it on X or Y.
    /// Flips are applied as negative scale so the mesh mirrors through its own pivot point.
    /// The prefab's own baked rotation and scale are preserved.
    /// </summary>
    public void Paint(Vector3Int cellPos, GameObject prefab, int rotation = 0)
    {
        if (prefab == null || !IsInBounds(cellPos)) return;

        var obj = InstantiateChild(prefab, UndoPaintCell);
        var localPos = CellLocalPosition(cellPos) + BuildMeshOffset(prefab, rotation, CellSize);
        var rot = BuildRotation(rotation) * prefab.transform.localRotation;
        obj.transform.SetLocalPositionAndRotation(localPos, rot);
        obj.transform.localScale = prefab.transform.localScale;
    }

    /// <summary>Places <paramref name="prefab"/> at an arbitrary world position within the grid bounds, with an optional rotation in degrees.</summary>
    public void PaintFree(Vector3 worldPosition, GameObject prefab, int rotation = 0)
    {
        if (prefab == null || !IsInWorldBounds(worldPosition)) return;

        var obj = InstantiateChild(prefab, UndoPaintFree);
        obj.transform.SetPositionAndRotation(worldPosition, BuildRotation(rotation) * prefab.transform.localRotation);
        obj.transform.localScale = prefab.transform.localScale;
    }

    /// <summary>Places <paramref name="prefab"/> at an arbitrary world position aligned to a surface normal, with an optional rotation in degrees.</summary>
    public void PaintFree(Vector3 worldPosition, GameObject prefab, Quaternion surfaceRotation, int rotation = 0)
    {
        if (prefab == null || !IsInWorldBounds(worldPosition)) return;

        var obj = InstantiateChild(prefab, UndoPaintFree);
        obj.transform.SetPositionAndRotation(worldPosition, surfaceRotation * BuildRotation(rotation) * prefab.transform.localRotation);
        obj.transform.localScale = prefab.transform.localScale;
    }

    /// <summary>
    /// Destroys <paramref name="target"/> and places <paramref name="prefab"/>
    /// at the same world position, with an optional rotation in degrees.
    /// </summary>
    public void Replace(GameObject target, GameObject prefab, int rotation = 0)
    {
        if (target == null || prefab == null) return;

        var worldPos = target.transform.position;
        DestroyObject(target);

        var obj = InstantiateChild(prefab, UndoReplace);
        obj.transform.SetPositionAndRotation(worldPos, BuildRotation(rotation) * prefab.transform.localRotation);
        obj.transform.localScale = prefab.transform.localScale;
    }

    /// <summary>Destroys a child object that belongs to this grid.</summary>
    public void EraseObject(GameObject obj)
    {
        if (obj == null) return;
        DestroyObject(obj);
    }

    /// <summary>Returns true if <paramref name="worldPosition"/> maps to a point inside the grid bounds.</summary>
    public bool IsInWorldBounds(Vector3 worldPosition)
    {
        var local = transform.InverseTransformPoint(worldPosition);
        return local.x >= 0 && local.y >= 0 &&
               local.x < Size.x * CellSize && local.y < Size.y * CellSize;
    }

    /// <summary>
    /// Returns the local-space position of the bottom-left corner of a grid cell.
    /// Meshes are then offset via BuildMeshOffset so their own bottom-left sits here.
    /// </summary>
    public Vector3 CellLocalPosition(Vector3Int cell) => new(
        cell.x * CellSize,
        cell.y * CellSize,
        cell.z * CellSize
    );

    /// <summary>Returns true if <paramref name="cellPos"/> lies within the grid bounds.</summary>
    private bool IsInBounds(Vector3Int cellPos) =>
        cellPos.x >= 0 && cellPos.y >= 0 && cellPos.x < Size.x && cellPos.y < Size.y;

    /// <summary>Returns a Z-axis rotation quaternion for the given angle in degrees (0, 90, 180, 270).</summary>
    public static Quaternion BuildRotation(int degrees) =>
        Quaternion.AngleAxis(degrees, Vector3.forward);

    /// <summary>
    /// Returns the footprint of <paramref name="prefab"/> in whole grid cells, accounting for rotation.
    /// At 90° or 270°, SizeX and SizeY are swapped.
    /// </summary>
    public static Vector2Int GetFootprintCells(GameObject prefab, float cellSize, int rotation = 0)
    {
        if (prefab == null) return Vector2Int.one;

        Vector2Int fp;
        var go = prefab.GetComponent<GridObject>();
        if (go != null)
        {
            // A baked 90°/270° Y rotation makes the object's SizeZ become its grid X width.
            float bakedY = prefab.transform.localEulerAngles.y;
            bool bakedYSwap = Mathf.Approximately(Mathf.Abs(Mathf.DeltaAngle(bakedY, 90f)), 0f)
                           || Mathf.Approximately(Mathf.Abs(Mathf.DeltaAngle(bakedY, 270f)), 0f);
            fp = bakedYSwap ? new Vector2Int(go.SizeZ, go.SizeY) : new Vector2Int(go.SizeX, go.SizeY);
        }
        else
        {
            // Only use the baked Z rotation when computing the footprint from mesh bounds —
            // baked X/Y rotations (e.g. 90° Y to face the camera) are visual-only.
            var bakedZRot = Quaternion.AngleAxis(prefab.transform.localEulerAngles.z, Vector3.forward);
            var b = GetLocalBounds(prefab);
            var corners = new[]
            {
                new Vector3(b.min.x, b.min.y, 0f), new Vector3(b.max.x, b.min.y, 0f),
                new Vector3(b.min.x, b.max.y, 0f), new Vector3(b.max.x, b.max.y, 0f),
            };
            var rMin = new Vector2(float.MaxValue, float.MaxValue);
            var rMax = new Vector2(float.MinValue, float.MinValue);
            foreach (var c in corners)
            {
                var r = bakedZRot * c;
                rMin.x = Mathf.Min(rMin.x, r.x); rMin.y = Mathf.Min(rMin.y, r.y);
                rMax.x = Mathf.Max(rMax.x, r.x); rMax.y = Mathf.Max(rMax.y, r.y);
            }
            fp = new Vector2Int(
                Mathf.Max(1, Mathf.RoundToInt((rMax.x - rMin.x) / cellSize)),
                Mathf.Max(1, Mathf.RoundToInt((rMax.y - rMin.y) / cellSize))
            );
        }

        int normalizedRot = ((rotation % 360) + 360) % 360;
        return (normalizedRot == 90 || normalizedRot == 270) ? new Vector2Int(fp.y, fp.x) : fp;
    }

    /// <summary>
    /// Returns a local-space XY offset to place <paramref name="prefab"/> with its PivotX/PivotY
    /// correctly anchored within its cell footprint, accounting for the applied rotation.
    /// </summary>
    public static Vector3 BuildMeshOffset(GameObject prefab, int rotation = 0, float cellSize = 1f)
    {
        if (prefab == null) return Vector3.zero;

        var bounds = GetLocalBounds(prefab);
        var go = prefab.GetComponent<GridObject>();
        var pivotX = go != null ? go.PivotX : GridObject.Pivot.Center;
        var pivotY = go != null ? go.PivotY : GridObject.Pivot.Center;

        // Pick the pivot point in prefab-local space based on PivotX/Y.
        float localPx = pivotX switch
        {
            GridObject.Pivot.Start => bounds.min.x,
            GridObject.Pivot.End   => bounds.max.x,
            _                      => bounds.center.x,
        };
        float localPy = pivotY switch
        {
            GridObject.Pivot.Start => bounds.min.y,
            GridObject.Pivot.End   => bounds.max.y,
            _                      => bounds.center.y,
        };

        // Only the baked Z rotation affects XY grid layout; X/Y baked rotations are purely visual
        // (e.g. a 90° Y to face the camera) and must not corrupt the 2D pivot math.
        var bakedZRot = Quaternion.AngleAxis(prefab.transform.localEulerAngles.z, Vector3.forward);
        var totalRot = BuildRotation(rotation) * bakedZRot;
        var meshPoint = totalRot * new Vector3(localPx, localPy, 0f);

        // Compute the anchor in footprint space using the *unrotated* footprint,
        // then rotate it by the user rotation around the footprint center so it
        // follows the mesh pivot correctly (e.g. PivotY=Start at 90° → right wall).
        var fpOrig = GetFootprintCells(prefab, cellSize, 0);
        var fpRot  = GetFootprintCells(prefab, cellSize, rotation);

        float origAx = pivotX switch
        {
            GridObject.Pivot.Start => 0f,
            GridObject.Pivot.End   => fpOrig.x * cellSize,
            _                      => fpOrig.x * cellSize * 0.5f,
        };
        float origAy = pivotY switch
        {
            GridObject.Pivot.Start => 0f,
            GridObject.Pivot.End   => fpOrig.y * cellSize,
            _                      => fpOrig.y * cellSize * 0.5f,
        };

        var fpOrigCenter = new Vector3(fpOrig.x * cellSize * 0.5f, fpOrig.y * cellSize * 0.5f, 0f);
        var fpRotCenter  = new Vector3(fpRot.x  * cellSize * 0.5f, fpRot.y  * cellSize * 0.5f, 0f);

        var anchorLocal = new Vector3(origAx, origAy, 0f);
        var anchorPoint = BuildRotation(rotation) * (anchorLocal - fpOrigCenter) + fpRotCenter;

        return anchorPoint - new Vector3(meshPoint.x, meshPoint.y, 0f);
    }

    // Returns the combined mesh bounds in prefab local space.
    private static Bounds GetLocalBounds(GameObject prefab)
    {
        var bounds = new Bounds();
        bool initialized = false;
        foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>())
        {
            if (mf == null || mf.sharedMesh == null) continue;
            var b = mf.sharedMesh.bounds;
            // Transform all 8 corners through mf → prefab local space.
            foreach (var corner in new[]
            {
                new Vector3(b.min.x, b.min.y, b.min.z), new Vector3(b.max.x, b.min.y, b.min.z),
                new Vector3(b.min.x, b.max.y, b.min.z), new Vector3(b.max.x, b.max.y, b.min.z),
                new Vector3(b.min.x, b.min.y, b.max.z), new Vector3(b.max.x, b.min.y, b.max.z),
                new Vector3(b.min.x, b.max.y, b.max.z), new Vector3(b.max.x, b.max.y, b.max.z),
            })
            {
                var localPt = prefab.transform.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (!initialized) { bounds = new Bounds(localPt, Vector3.zero); initialized = true; }
                else bounds.Encapsulate(localPt);
            }
        }
        return bounds;
    }

    private GameObject InstantiateChild(GameObject prefab, string undoName)
    {
#if UNITY_EDITOR
        var obj = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, transform);
        UnityEditor.Undo.RegisterCreatedObjectUndo(obj, undoName);
        return obj;
#else
        return Instantiate(prefab, transform);
#endif
    }

    private static void DestroyObject(GameObject obj)
    {
        if (obj == null) return;
#if UNITY_EDITOR
        UnityEditor.Undo.DestroyObjectImmediate(obj);
#else
        Destroy(obj);
#endif
    }
}
