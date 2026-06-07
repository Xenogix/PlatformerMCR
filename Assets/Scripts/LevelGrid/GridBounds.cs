using UnityEngine;

// BoxCollider2D may live on this GameObject or on a child. When the collider is
// on a child, a Kinematic Rigidbody2D on this GameObject is required for triggers
// to bubble up.
[ExecuteAlways]
[RequireComponent(typeof(GridLevelLayout))]
public class GridBounds : MonoBehaviour
{
    public float Padding = 0f;

    private GridLevelLayout _grid;
    private BoxCollider2D _box;

private void OnEnable()
{
    UpdateRefs();
    if (_grid == null)
    {
        Debug.LogError("GridLevelLayout not found on " + gameObject.name);
        return;
    }
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

    private void OnTriggerExit2D(Collider2D other)
    {
        // Kill all entities that leave the bounds.
        if (other.TryGetComponent<Entity>(out var entity))
            entity.Kill();
    }

    private void UpdateRefs()
    {
        if (_grid == null) _grid = GetComponent<GridLevelLayout>();
        if (_box == null) _box = GetComponentInChildren<BoxCollider2D>(true);
    }

    public void Refresh()
    {
        float w = _grid.Size.x * _grid.CellSize;
        float h = _grid.Size.y * _grid.CellSize;

        _box.isTrigger = true;
        _box.size = new Vector2(w + Padding * 2f, h + Padding * 2f);
        _box.offset = new Vector2(w * 0.5f, h * 0.5f);
    }
}
