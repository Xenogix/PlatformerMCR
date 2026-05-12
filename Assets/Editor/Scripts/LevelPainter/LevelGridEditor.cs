using System.Reflection;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LevelGrid))]
public class LevelGridEditor : Editor
{
    private static readonly MethodInfo s_IntersectRayMesh = typeof(HandleUtility).GetMethod("IntersectRayMesh", BindingFlags.Static | BindingFlags.NonPublic);

    // Colors
    private readonly Color borderColor = new(0.498f, 1f, 0.831f, 0.7f);
    private readonly Color cellColor = new(0.498f, 1f, 0.831f, 0.5f);
    private readonly Color highlightFillColor = new(1f, 0.922f, 0.016f, 0.5f);
    private readonly Color highlightOutlineColor = new(1f, 0.922f, 0.016f, 0.7f);
    private readonly Color replaceColor = new(1f, 0.4f, 0.1f, 0.5f);
    private readonly Color eraseColor = new(1f, 0.1f, 0.1f, 0.5f);
    private readonly Color freePaintDiscColor = new(0f, 1f, 1f, 0.5f);

    // Preview instance
    private GameObject previewInstance;
    private GameObject lastPreviewPrefab;

    // Free paint state
    private Vector3 freePaintPosition;
    private bool freePaintHitSurface;
    private Quaternion freePaintSurfaceRotation = Quaternion.identity;

    // Hovered object for replace mode
    private GameObject hoveredObject;

    private void OnDisable() => DestroyPreview();

    private void OnSceneGUI()
    {
        var grid = (LevelGrid)target;
        var session = LevelPainterWindow.ActiveSession;
        if (grid == null || session == null) return;

        if (session.ShowGrid)
            DrawGrid(grid, session.ZLayer);

        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        if (Event.current.rawType == EventType.KeyDown && Event.current.keyCode == KeyCode.R)
        {
            session.CycleRotation();
            Event.current.Use();
            SceneView.RepaintAll();
            return;
        }

        var ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
        // XY plane: normal is Vector3.forward (Z axis), offset by ZLayer
        var plane = new Plane(Vector3.forward, new Vector3(0f, 0f, grid.transform.position.z + grid.CellSize * session.ZLayer));
        if (!plane.Raycast(ray, out float distance)) return;

        var worldPos = ray.GetPoint(distance);

        Vector3 targetPosition;
        Vector3Int cellPos;
        Quaternion previewRot;

        if (session.SnapToGrid)
        {
            cellPos = WorldToCell(grid, worldPos, session.ZLayer);
            targetPosition = CellToWorld(grid, cellPos) + LevelGrid.BuildMeshOffset(session.SelectedPrefab, session.Rotation, grid.CellSize);
            previewRot = LevelGrid.BuildRotation(session.Rotation);
            DrawFootprintHighlight(grid, cellPos, session.SelectedPrefab, session.Rotation);
        }
        else
        {
            freePaintHitSurface = TryRaycastGridChildren(grid, ray, out freePaintPosition, out freePaintSurfaceRotation);
            targetPosition = freePaintHitSurface ? freePaintPosition : worldPos;
            cellPos = WorldToCell(grid, targetPosition, session.ZLayer);
            previewRot = freePaintHitSurface && session.AlignToNormal
                ? freePaintSurfaceRotation * LevelGrid.BuildRotation(session.Rotation)
                : LevelGrid.BuildRotation(session.Rotation);

            if (freePaintHitSurface)
                DrawFreePositionHighlight(freePaintPosition, freePaintSurfaceRotation, grid.CellSize * 0.3f);
        }

        bool isPainting = session.Mode.HasFlag(LevelPaintMode.Paint) || session.Mode.HasFlag(LevelPaintMode.Replace);
        UpdatePreview(targetPosition, previewRot, session.SelectedPrefab, isPainting);

        if (session.Mode.HasFlag(LevelPaintMode.Replace) || session.Mode.HasFlag(LevelPaintMode.Erase))
        {
            if (Event.current.type == EventType.MouseMove || Event.current.type == EventType.MouseDrag)
            {
                var picked = HandleUtility.PickGameObject(Event.current.mousePosition, false);
                hoveredObject = (picked != null && picked.transform.IsChildOf(grid.transform))
                    ? ResolveDirectChild(grid, picked)
                    : null;
            }

            if (hoveredObject != null)
            {
                if (session.Mode.HasFlag(LevelPaintMode.Replace))
                    DrawReplaceHighlight(hoveredObject);
                else
                    DrawEraseHighlight(hoveredObject);
            }
        }

        if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
        {
            if (session.Mode.HasFlag(LevelPaintMode.Paint))
            {
                if (session.SnapToGrid)
                    grid.Paint(cellPos, session.SelectedPrefab, session.Rotation);
                else if (freePaintHitSurface && session.AlignToNormal)
                    grid.PaintFree(targetPosition, session.SelectedPrefab, freePaintSurfaceRotation, session.Rotation);
                else
                    grid.PaintFree(targetPosition, session.SelectedPrefab, session.Rotation);
            }
            else if (session.Mode.HasFlag(LevelPaintMode.Erase))
            {
                var picked = HandleUtility.PickGameObject(Event.current.mousePosition, false);
                if (picked != null && picked.transform.IsChildOf(grid.transform))
                    grid.EraseObject(ResolveDirectChild(grid, picked));
            }
            else if (session.Mode.HasFlag(LevelPaintMode.Replace))
            {
                if (hoveredObject != null)
                    grid.Replace(hoveredObject, session.SelectedPrefab, session.Rotation);
            }
            Event.current.Use();
        }

        if (!session.Mode.HasFlag(LevelPaintMode.Replace) && !session.Mode.HasFlag(LevelPaintMode.Erase) && hoveredObject != null)
            hoveredObject = null;

        if (Event.current.type == EventType.MouseMove || Event.current.type == EventType.MouseDrag)
            SceneView.RepaintAll();
    }

    private static Vector3Int WorldToCell(LevelGrid grid, Vector3 worldPos, int zLayer)
    {
        var local = grid.transform.InverseTransformPoint(worldPos);
        return new Vector3Int(
            Mathf.FloorToInt(local.x / grid.CellSize),
            Mathf.FloorToInt(local.y / grid.CellSize),
            zLayer
        );
    }

    private static Vector3 CellToWorld(LevelGrid grid, Vector3Int cell)
        => grid.transform.TransformPoint(grid.CellLocalPosition(cell));

    private void DrawGrid(LevelGrid grid, int zLayer)
    {
        float w = grid.Size.x * grid.CellSize;
        float h = grid.Size.y * grid.CellSize;

        Handles.color = cellColor;
        for (int x = 0; x <= grid.Size.x; x++)
            Handles.DrawLine(
                grid.transform.TransformPoint(new Vector3(x * grid.CellSize, 0, zLayer * grid.CellSize)),
                grid.transform.TransformPoint(new Vector3(x * grid.CellSize, h, zLayer * grid.CellSize)));
        for (int y = 0; y <= grid.Size.y; y++)
            Handles.DrawLine(
                grid.transform.TransformPoint(new Vector3(0, y * grid.CellSize, zLayer * grid.CellSize)),
                grid.transform.TransformPoint(new Vector3(w, y * grid.CellSize, zLayer * grid.CellSize)));

        Handles.color = borderColor;
        Handles.DrawPolyLine(
            grid.transform.TransformPoint(new Vector3(0, 0, zLayer * grid.CellSize)),
            grid.transform.TransformPoint(new Vector3(w, 0, zLayer * grid.CellSize)),
            grid.transform.TransformPoint(new Vector3(w, h, zLayer * grid.CellSize)),
            grid.transform.TransformPoint(new Vector3(0, h, zLayer * grid.CellSize)),
            grid.transform.TransformPoint(new Vector3(0, 0, zLayer * grid.CellSize))
        );
    }

    private void DrawFootprintHighlight(LevelGrid grid, Vector3Int cellPos, GameObject prefab, int rotation = 0)
    {
        var fp = LevelGrid.GetFootprintCells(prefab, grid.CellSize, rotation);
        float w = fp.x * grid.CellSize;
        float h = fp.y * grid.CellSize;
        var origin = CellToWorld(grid, cellPos);
        Handles.DrawSolidRectangleWithOutline(
            new[] {
                origin,
                origin + new Vector3(w, 0, 0),
                origin + new Vector3(w, h, 0),
                origin + new Vector3(0, h, 0)
            },
            highlightFillColor,
            highlightOutlineColor
        );
    }

    private void DrawReplaceHighlight(GameObject go)
    {
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (mf == null || mf.sharedMesh == null) continue;
            Handles.color = replaceColor;
            Handles.matrix = mf.transform.localToWorldMatrix;
            Handles.DrawWireCube(mf.sharedMesh.bounds.center, mf.sharedMesh.bounds.size * 1.05f);
        }
        Handles.matrix = Matrix4x4.identity;
    }

    private void DrawEraseHighlight(GameObject go)
    {
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (mf == null || mf.sharedMesh == null) continue;
            Handles.color = eraseColor;
            Handles.matrix = mf.transform.localToWorldMatrix;
            Handles.DrawWireCube(mf.sharedMesh.bounds.center, mf.sharedMesh.bounds.size * 1.05f);
        }
        Handles.matrix = Matrix4x4.identity;
    }

    private void DrawFreePositionHighlight(Vector3 position, Quaternion rotation, float radius)
    {
        var normal = rotation * Vector3.forward;
        Handles.color = freePaintDiscColor;
        Handles.DrawWireDisc(position, normal, radius);
        Handles.DrawSolidDisc(position, normal, radius * 0.5f);
    }

    private void UpdatePreview(Vector3 position, Quaternion rotation, GameObject prefab, bool visible)
    {
        if (!visible || prefab == null)
        {
            DestroyPreview();
            return;
        }

        if (previewInstance == null || lastPreviewPrefab != prefab)
        {
            DestroyPreview();
            previewInstance   = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            SetHideFlagsRecursive(previewInstance, HideFlags.HideAndDontSave);
            lastPreviewPrefab = prefab;
        }

        previewInstance.transform.SetPositionAndRotation(position, rotation * prefab.transform.localRotation);
        previewInstance.transform.localScale = prefab.transform.localScale;
    }

    private void DestroyPreview()
    {
        if (previewInstance == null) return;
        DestroyImmediate(previewInstance);
        previewInstance = null;
    }

    private static void SetHideFlagsRecursive(GameObject go, HideFlags flags)
    {
        go.hideFlags = flags;
        foreach (Transform child in go.transform)
            SetHideFlagsRecursive(child.gameObject, flags);
    }

    private static bool TryRaycastGridChildren(LevelGrid grid, Ray ray, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        float bestDist = float.MaxValue;
        Vector3 bestPoint = Vector3.zero;
        Vector3 bestNormal = Vector3.forward;
        bool hit = false;

        foreach (var mf in grid.GetComponentsInChildren<MeshFilter>())
        {
            if (mf == null || mf.sharedMesh == null) continue;

            var args = new object[] { ray, mf.sharedMesh, mf.transform.localToWorldMatrix, null };
            if (s_IntersectRayMesh != null && (bool)s_IntersectRayMesh.Invoke(null, args))
            {
                var info = (RaycastHit)args[3];
                if (info.distance < bestDist)
                {
                    bestDist = info.distance;
                    bestPoint = info.point;
                    bestNormal = info.normal;
                    hit = true;
                }
            }
        }

        if (!hit) return false;

        position = bestPoint;
        // Align the surface normal to Vector3.forward (the XY plane normal); flips are applied separately.
        rotation = Quaternion.FromToRotation(Vector3.forward, bestNormal);
        return true;
    }

    /// <summary>
    /// Walks up the hierarchy to find the direct child of <paramref name="grid"/> that contains <paramref name="obj"/>.
    /// </summary>
    private static GameObject ResolveDirectChild(LevelGrid grid, GameObject obj)
    {
        var t = obj.transform;
        while (t != null && t.parent != grid.transform)
            t = t.parent;
        return t != null ? t.gameObject : null;
    }
}
