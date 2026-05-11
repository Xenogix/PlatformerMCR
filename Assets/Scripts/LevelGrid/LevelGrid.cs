using UnityEngine;

public class LevelGrid : MonoBehaviour
{
    // Inspector fields
    public Vector2Int Size = new(10, 10);
    public float CellSize = 1f;

    // Undo action names
    private const string UndoPaintCell = "Paint Level Cell";
    private const string UndoPaintFree = "Paint Level Free";
    private const string UndoReplace = "Replace Level Object";

    /// <summary>
    /// Places <paramref name="prefab"/> at the given grid cell, optionally flipping it on X or Y.
    /// Flips are applied as negative scale so the mesh mirrors through its own pivot point.
    /// The prefab's own baked rotation and scale are preserved.
    /// </summary>
    public void Paint(Vector3Int cellPos, GameObject prefab, bool flipX = false, bool flipY = false)
    {
        if (prefab == null || !IsInBounds(cellPos)) return;

        var obj = InstantiateChild(prefab, UndoPaintCell);
        var localPos = CellLocalPosition(cellPos) + BuildMeshOffset(prefab, flipX, flipY);
        obj.transform.SetLocalPositionAndRotation(localPos, prefab.transform.localRotation);
        obj.transform.localScale = BuildFlipScale(prefab, flipX, flipY);
    }

    /// <summary>Places <paramref name="prefab"/> at an arbitrary world position within the grid bounds, optionally flipping it on X or Y.</summary>
    public void PaintFree(Vector3 worldPosition, GameObject prefab, bool flipX = false, bool flipY = false)
    {
        if (prefab == null || !IsInWorldBounds(worldPosition)) return;

        var obj = InstantiateChild(prefab, UndoPaintFree);
        obj.transform.SetPositionAndRotation(worldPosition, prefab.transform.localRotation);
        obj.transform.localScale = BuildFlipScale(prefab, flipX, flipY);
    }

    /// <summary>Places <paramref name="prefab"/> at an arbitrary world position aligned to a surface normal, optionally flipping it on X or Y.</summary>
    public void PaintFree(Vector3 worldPosition, GameObject prefab, Quaternion surfaceRotation, bool flipX = false, bool flipY = false)
    {
        if (prefab == null || !IsInWorldBounds(worldPosition)) return;

        var obj = InstantiateChild(prefab, UndoPaintFree);
        // Compose the prefab's own baked rotation with the surface-aligned rotation.
        obj.transform.SetPositionAndRotation(worldPosition, prefab.transform.localRotation * surfaceRotation);
        obj.transform.localScale = BuildFlipScale(prefab, flipX, flipY);
    }

    /// <summary>
    /// Destroys <paramref name="target"/> and places <paramref name="prefab"/>
    /// at the same world position, optionally flipping it on X or Y.
    /// </summary>
    public void Replace(GameObject target, GameObject prefab, bool flipX = false, bool flipY = false)
    {
        if (target == null || prefab == null) return;

        var worldPos = target.transform.position;
        DestroyObject(target);

        var obj = InstantiateChild(prefab, UndoReplace);
        obj.transform.SetPositionAndRotation(worldPos, prefab.transform.localRotation);
        obj.transform.localScale = BuildFlipScale(prefab, flipX, flipY);
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

    /// <summary>
    /// Builds a local scale that mirrors the prefab along world X (flipX) and/or world Y (flipY).
    /// Because the prefab may have a baked rotation (e.g. 90° Y), its local axes do not
    /// necessarily align with world axes.  We find which local axis is most aligned with
    /// world X / world Y and negate that component, so the flip is always a visible
    /// horizontal/vertical mirror regardless of the prefab's baked rotation.
    /// </summary>
    public static Vector3 BuildFlipScale(GameObject prefab, bool flipX, bool flipY)
    {
        var s = prefab.transform.localScale;
        if (!flipX && !flipY) return s;

        var rot = prefab.transform.localRotation;
        var invRot = Quaternion.Inverse(rot);

        float sx = s.x, sy = s.y, sz = s.z;

        if (flipX)
            NegateAxis(invRot * Vector3.right, ref sx, ref sy, ref sz);

        if (flipY)
            NegateAxis(invRot * Vector3.up, ref sx, ref sy, ref sz);

        return new Vector3(sx, sy, sz);
    }

    // Negates the scale component whose local axis is most aligned with the given direction.
    private static void NegateAxis(Vector3 localDir, ref float sx, ref float sy, ref float sz)
    {
        float ax = Mathf.Abs(localDir.x), ay = Mathf.Abs(localDir.y), az = Mathf.Abs(localDir.z);
        if (ax >= ay && ax >= az) sx = -sx;
        else if (ay >= ax && ay >= az) sy = -sy;
        else sz = -sz;
    }

    /// <summary>
    /// Returns a local-space XY offset so that the bottom-left of <paramref name="prefab"/>'s mesh
    /// sits at the cell's bottom-left corner, regardless of where the mesh pivot is.
    /// Without flip: rendered min = pivot + min → offset = -min (brings min to 0).
    /// With    flip: scale negated, rendered min = pivot - max → offset = +max (brings -max+max to 0).
    /// </summary>
    public static Vector3 BuildMeshOffset(GameObject prefab, bool flipX, bool flipY)
    {
        if (prefab == null) return Vector3.zero;

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>())
        {
            if (mf == null || mf.sharedMesh == null) continue;

            var b = mf.sharedMesh.bounds;
            var corners = new Vector3[]
            {
                new(b.min.x, b.min.y, b.min.z), new(b.max.x, b.min.y, b.min.z),
                new(b.min.x, b.max.y, b.min.z), new(b.max.x, b.max.y, b.min.z),
                new(b.min.x, b.min.y, b.max.z), new(b.max.x, b.min.y, b.max.z),
                new(b.min.x, b.max.y, b.max.z), new(b.max.x, b.max.y, b.max.z),
            };
            foreach (var corner in corners)
            {
                var localPt = prefab.transform.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (localPt.x < minX) minX = localPt.x;
                if (localPt.x > maxX) maxX = localPt.x;
                if (localPt.y < minY) minY = localPt.y;
                if (localPt.y > maxY) maxY = localPt.y;
            }
        }

        if (minX == float.MaxValue) return Vector3.zero;

        float offsetX = flipX ? maxX : -minX;
        float offsetY = flipY ? maxY : -minY;
        return new Vector3(offsetX, offsetY, 0f);
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
