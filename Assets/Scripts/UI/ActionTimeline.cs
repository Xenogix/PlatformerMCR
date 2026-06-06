using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Bottom-of-screen timeline of recorded actions, one lane per character (P1 + clones), with a
// shared playhead marking the current time — think Super Time Force. The clone/Director system
// drives it: add a lane when a clone is created, grow its progress as it records/replays, drop
// ticks on key events, and move the playhead with the level clock.
//
// Lanes are instantiated from a LaneView template so layout is authored once. The playhead is a
// thin vertical marker spanning the lanes; its horizontal position is driven by anchors (0..1).
public class ActionTimeline : MonoBehaviour
{
    [Tooltip("Container holding the lane rows (typically with a VerticalLayoutGroup).")]
    [SerializeField] private RectTransform lanesContainer;
    [Tooltip("Disabled LaneView template, child of lanesContainer, cloned per lane.")]
    [SerializeField] private LaneView laneTemplate;
    [Tooltip("Vertical marker spanning the lanes; its anchorX is driven 0..1.")]
    [SerializeField] private RectTransform playhead;
    [Tooltip("Left inset (px) so the playhead lines up with the lane bars, which start after the " +
             "label column. Must match the lane Track's left offset (offsetMin.x).")]
    [SerializeField] private float playheadLeftInset = 40f;

    private readonly List<LaneView> _lanes = new();

    // Last requested playhead time (0..1). Cached so it can be re-applied once the canvas has a valid
    // width — on a freshly loaded scene the Screen Space - Camera rect is still 0 during Start.
    private float _playheadT;
    private bool _playheadPending;

    public int LaneCount => _lanes.Count;

    private void Start()
    {
        UpdatePlayheadHeight();
        SetPlayhead(0f);
    }

    // Retry seating the playhead until the canvas has been laid out (parent width > 0). Without this,
    // a scene loaded mid-game seats it at x=0 (hidden behind the CRT's edge curvature/overscan).
    private void LateUpdate()
    {
        if (_playheadPending) ApplyPlayhead();
    }

    // Adds a lane (e.g. on clone creation) and returns its index for later updates.
    public int AddLane(string label, Color color)
    {
        if (laneTemplate == null || lanesContainer == null) return -1;

        var lane = Instantiate(laneTemplate, lanesContainer);
        lane.gameObject.SetActive(true);
        lane.SetLabel(label);
        lane.SetColor(color);
        lane.SetProgress(0f);
        _lanes.Add(lane);
        UpdatePlayheadHeight();
        return _lanes.Count - 1;
    }

    // Sizes the playhead to the stacked lanes (centered), so it doesn't span the whole timeline
    // box when only a few clones exist.
    private void UpdatePlayheadHeight()
    {
        if (playhead == null) return;

        float height = 0f;
        int n = _lanes.Count;
        if (n > 0)
        {
            float laneHeight = laneTemplate != null
                ? ((RectTransform)laneTemplate.transform).rect.height
                : 24f;
            float spacing = lanesContainer != null
                            && lanesContainer.TryGetComponent(out VerticalLayoutGroup vlg)
                ? vlg.spacing
                : 0f;
            height = n * laneHeight + (n - 1) * spacing;
        }

        playhead.anchorMin = new Vector2(playhead.anchorMin.x, 0.5f);
        playhead.anchorMax = new Vector2(playhead.anchorMax.x, 0.5f);
        playhead.sizeDelta = new Vector2(playhead.sizeDelta.x, height);
    }

    public void SetLaneProgress(int lane, float t01)
    {
        if (lane >= 0 && lane < _lanes.Count) _lanes[lane].SetProgress(t01);
    }

    public void AddEvent(int lane, float t01)
    {
        if (lane >= 0 && lane < _lanes.Count) _lanes[lane].AddTick(t01);
    }

    // Moves the shared playhead. t01 is normalized over the recording window (0 = start, 1 = end).
    public void SetPlayhead(float t01)
    {
        _playheadT = Mathf.Clamp01(t01);
        ApplyPlayhead();
    }

    // Positions the playhead by anchor x, mapped into [leftInset, fullWidth] so it lines up with the
    // lane bars (which start after the label column). If the parent width isn't valid yet (canvas not
    // laid out), flags itself to retry in LateUpdate rather than seating at the hidden left edge.
    private void ApplyPlayhead()
    {
        if (playhead == null || playhead.parent is not RectTransform parent) return;

        float w = parent.rect.width;
        if (w <= 0f) { _playheadPending = true; return; }

        float x = (playheadLeftInset + _playheadT * (w - playheadLeftInset)) / w;
        playhead.anchorMin = new Vector2(x, playhead.anchorMin.y);
        playhead.anchorMax = new Vector2(x, playhead.anchorMax.y);
        playhead.anchoredPosition = new Vector2(0f, playhead.anchoredPosition.y);
        _playheadPending = false;
    }

    // Resets the timeline (e.g. on level restart).
    public void ClearLanes()
    {
        foreach (var lane in _lanes)
            if (lane != null) Destroy(lane.gameObject);
        _lanes.Clear();
        UpdatePlayheadHeight();
    }
}
