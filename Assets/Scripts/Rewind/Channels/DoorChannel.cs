using UnityEngine;

public sealed class DoorChannel : RewindChannel<bool>
{
    private Door _door;

    protected override void Awake()
    {
        base.Awake();
        _door = GetComponent<Door>();
    }

    protected override IHistory<bool> NewHistory() => new SparseHistory<bool>();

    protected override bool Read() => _door.IsOpen;

    protected override void Write(bool isOpen) => _door.SetOpen(isOpen);
}