using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives one shadow body: a pure position write per clock tick, NO physics — so it retraces the
/// recorded path exactly, with no drift. Addressed by ABSOLUTE clock tick (like
/// <see cref="ClonePlayback"/>), so if the player rewinds a live attempt the shadow rewinds for
/// free. Outside its [spawnTick, spawnTick+count) window the body simply hides.
/// </summary>
public sealed class ShadowPlayback : MonoBehaviour, ITickable
{
    private IReadOnlyList<Vector2> _positions;
    private int _spawnTick;
    private Renderer[] _renderers;
    private bool _visible = true;

    private void Awake()
    {
        // Cache renderers (incl. inactive/disabled children) and hide immediately, so the body
        // doesn't flash at the prefab origin for the frame before its first Tick places it.
        _renderers = GetComponentsInChildren<Renderer>(true);
        SetVisible(false);
    }

    /// <summary>Begin retracing the given path; its first position lands on <paramref name="spawnTick"/>.</summary>
    public void Play(int spawnTick, IReadOnlyList<Vector2> positions)
    {
        _spawnTick = spawnTick;
        _positions = positions;
    }

    private void OnEnable() => GameClock.Instance.Register(this);

    private void OnDisable()
    {
        if (GameClock.HasInstance) GameClock.Instance.Unregister(this);
    }

    public void Tick(int tick, float dt)
    {
        if (_positions == null) return;

        int i = tick - _spawnTick;
        bool visible = i >= 0 && i < _positions.Count;
        SetVisible(visible);
        if (visible)
            transform.position = new Vector3(_positions[i].x, _positions[i].y, transform.position.z); // preserve z-plane
    }

    private void SetVisible(bool visible)
    {
        if (visible == _visible || _renderers == null) return;
        _visible = visible;
        foreach (Renderer r in _renderers)
            if (r != null) r.enabled = visible;
    }
}
