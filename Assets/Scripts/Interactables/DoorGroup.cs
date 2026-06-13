using UnityEngine;

public class DoorGroup : MonoBehaviour
{
    [Tooltip("Doors driven by this group. Auto-filled from children when left empty.")]
    [SerializeField] private Door[] doors;

    private void Awake()
    {
        if (doors == null || doors.Length == 0)
            doors = GetComponentsInChildren<Door>(includeInactive: true);
    }

    public void Open() => SetOpen(true);

    public void Close() => SetOpen(false);

    public void Toggle() => SetOpen(!AllOpen());

    public void SetOpen(bool open)
    {
        if (doors == null)
            return;

        foreach (var door in doors)
            if (door != null)
                door.SetOpen(open);
    }

    private bool AllOpen()
    {
        if (doors == null)
            return false;

        foreach (var door in doors)
            if (door != null && !door.IsOpen())
                return false;

        return true;
    }
}
