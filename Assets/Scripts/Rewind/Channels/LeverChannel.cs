public sealed class LeverChannel : RewindChannel<bool>
{
    private Lever _lever;

    protected override void Awake()
    {
        base.Awake();
        _lever = GetComponent<Lever>();
    }

    protected override IHistory<bool> NewHistory() => new SparseHistory<bool>();

    protected override bool Read() => _lever.IsOn;

    // Restore silently: the lever's wired targets are rewound by their own channels.
    protected override void Write(bool isOn) => _lever.RestoreState(isOn);
}
