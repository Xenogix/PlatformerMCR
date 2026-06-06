using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Bottom-of-screen timeline of recorded actions, one lane per character (P1 + clones), with a shared
// playhead marking the current time — think Super Time Force. The Director drives it: add a lane per
// clone, grow its progress, drop ticks, move the playhead.
//
// Sizing is fully layout-driven: lanesContainer uses a VerticalLayoutGroup + ContentSizeFitter so
// the stack grows with the clones, and the playhead is a child of it, vertically stretched
// (LayoutElement: Ignore Layout), so it always matches the stack. This class only drives the
// playhead's horizontal position plus the selection/visibility plumbing.
public class ActionTimeline : MonoBehaviour
{
    [Tooltip("Container holding the lane rows (VerticalLayoutGroup + ContentSizeFitter).")]
    [SerializeField] private RectTransform lanesContainer;
    [Tooltip("Disabled LaneView template, child of lanesContainer, cloned per lane.")]
    [SerializeField] private LaneView laneTemplate;
    [Tooltip("Vertical marker; child of lanesContainer (Ignore Layout, vertically stretched). Its " +
             "anchorX is driven 0..1; its height comes from the layout.")]
    [SerializeField] private RectTransform playhead;
    [Tooltip("Left inset (px) so the playhead lines up with the lane bars, which start after the " +
             "label column. Match it to the lane Track's left offset.")]
    [SerializeField] private float playheadLeftInset = 40f;
    [Tooltip("Optional \"New Timeline\"/Add button to start a new recording. Place it yourself; it's " +
             "wired for navigation + Submit only.")]
    [SerializeField] private Button newTimelineButton;
    [Tooltip("Optional CanvasGroup used to show/hide the timeline. Auto-added if left empty.")]
    [SerializeField] private CanvasGroup canvasGroup;

    private readonly List<LaneView> _lanes = new();

    public int LaneCount => _lanes.Count;

    // Raised on Submit (keyboard/gamepad) so the Director can act: a lane index to load/replay that
    // clone, or NewTimelineSubmitted to start a fresh recording. The timeline stays agnostic.
    public event Action<int> LaneSubmitted;
    public event Action NewTimelineSubmitted;

    private void Awake()
    {
        if (newTimelineButton != null)
        {
            SetAutoNavigation(newTimelineButton);
            newTimelineButton.onClick.AddListener(() => NewTimelineSubmitted?.Invoke());
        }
    }

    private void Start()
    {
        Canvas.ForceUpdateCanvases(); // valid parent width before the first seat
        SetPlayhead(0f);
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

        if (lane.Button != null)
        {
            int index = _lanes.Count;
            SetAutoNavigation(lane.Button);
            lane.Button.onClick.AddListener(() => LaneSubmitted?.Invoke(index));
        }

        _lanes.Add(lane);
        return _lanes.Count - 1;
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
    // Drives only the horizontal anchor (mapped into [leftInset, fullWidth] so it lines up with the
    // lane bars); the height is layout-driven (vertically-stretched child of lanesContainer).
    public void SetPlayhead(float t01)
    {
        if (playhead == null) return;
        float t = Mathf.Clamp01(t01);
        float x = t;
        if (playhead.parent is RectTransform parent && parent.rect.width > 0f)
        {
            float w = parent.rect.width;
            x = (playheadLeftInset + t * (w - playheadLeftInset)) / w;
        }
        playhead.anchorMin = new Vector2(x, playhead.anchorMin.y);
        playhead.anchorMax = new Vector2(x, playhead.anchorMax.y);
        playhead.anchoredPosition = new Vector2(0f, playhead.anchoredPosition.y);
    }

    // Resets the timeline (e.g. on level restart).
    public void ClearLanes()
    {
        foreach (var lane in _lanes)
            if (lane != null) Destroy(lane.gameObject);
        _lanes.Clear();
    }

    // Show/hide the whole timeline via a CanvasGroup (keeps the GameObject active). Hiding also makes
    // it non-interactable and drops the EventSystem focus.
    public void SetVisible(bool visible)
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();

        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;

        if (!visible && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    // Focuses the first lane (or the New button if there are no lanes) when the pause menu opens, so
    // keyboard/gamepad navigation has a starting point.
    public void FocusSelection()
    {
        var es = EventSystem.current;
        if (es == null) return;
        GameObject target =
            _lanes.Count > 0 && _lanes[0].Button != null ? _lanes[0].Button.gameObject :
            newTimelineButton != null ? newTimelineButton.gameObject : null;
        if (target != null) es.SetSelectedGameObject(target);
    }

    private static void SetAutoNavigation(Selectable selectable)
    {
        var nav = selectable.navigation;
        nav.mode = Navigation.Mode.Automatic; // neighbours computed by position → handles the dynamic list
        selectable.navigation = nav;
    }
}
