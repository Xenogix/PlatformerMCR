using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(GridLayout))]
[RequireComponent(typeof(BoxCollider))]
public class GridBounds : MonoBehaviour
{
    // Inspector Fields
    public float Depth = 10f;
    public float DepthCenter = 0f;
    public float Padding = 0f;

    // Script references
    private GridLayout _grid;
    private BoxCollider _box;

    private void OnEnable()
    {
        UpdateRefs();
        _grid.FieldChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        if (_grid != null) _grid.FieldChanged -= Refresh;
    }

    private void OnValidate()
    {
        UpdateRefs();
        if (_grid != null && _box != null) Refresh();
    }

    private void OnTriggerExit(Collider other)
    {
        // Kill all entities that leave the bounds
        if (other.TryGetComponent<Entity>(out var entity))
            entity.Kill();
    }

    private void UpdateRefs()
    {
        if (_grid == null) _grid = GetComponent<GridLayout>();
        if (_box == null) _box = GetComponent<BoxCollider>();
    }

    public void Refresh()
    {
        float w = _grid.Size.x * _grid.CellSize;
        float h = _grid.Size.y * _grid.CellSize;

        _box.isTrigger = true;
        _box.size = new Vector3(w + Padding * 2f, h + Padding * 2f, Depth);
        _box.center = new Vector3(w * 0.5f, h * 0.5f, DepthCenter);
    }
}
