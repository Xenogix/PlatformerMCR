using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
    [Tooltip("Optional CanvasGroup used to show/hide the timeline. Auto-added if left empty.")]
    [SerializeField] private CanvasGroup canvasGroup;
    [Tooltip("Colour of the translucent overlay that dims the 'future' (everything to the right of " +
             "the playhead — the states you'd discard/replay if you confirmed here).")]
    [SerializeField] private Color futureMaskColor = new(0f, 0f, 0f, 0.55f);
    [Tooltip("Hint line shown inside the timeline; its key tokens are filled in at runtime by the " +
             "RewindDirector from the actual bindings (so it stays correct if controls are rebound).")]
    [SerializeField] private TMP_Text hintLabel;

    private readonly List<LaneView> _lanes = new();
    private RectTransform _futureMask; // dims [playhead .. right edge]; built at runtime

    private void Start()
    {
        Canvas.ForceUpdateCanvases(); // valid parent width before the first seat
        BuildFutureMask();
        SetPlayhead(0f);
    }

    // The "future" dim: a translucent overlay spanning [playhead, right edge] across all lanes,
    // showing the recorded states ahead of the playhead as greyed-out. Built in code so no prefab
    // wiring is needed.
    private void BuildFutureMask()
    {
        if (_futureMask != null || playhead == null || playhead.parent is not RectTransform parent) return;

        var go = new GameObject("FutureMask", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _futureMask = go.GetComponent<RectTransform>();
        _futureMask.SetParent(parent, false);
        _futureMask.anchorMin = new Vector2(1f, 0f); // starts empty (playhead at present)
        _futureMask.anchorMax = new Vector2(1f, 1f);
        _futureMask.offsetMin = Vector2.zero;
        _futureMask.offsetMax = Vector2.zero;

        var img = go.GetComponent<Image>();
        img.color = futureMaskColor;
        img.raycastTarget = false;

        go.AddComponent<LayoutElement>().ignoreLayout = true; // not part of the VerticalLayoutGroup stack

        BringOverlaysToFront();
    }

    // Keep the future mask, then the playhead, as the last siblings so they draw above the lanes
    // (sibling order = draw order). Called after lanes are added.
    private void BringOverlaysToFront()
    {
        if (_futureMask != null) _futureMask.SetAsLastSibling();
        if (playhead != null) playhead.SetAsLastSibling();
    }

    // Adds a lane (on clone creation) and returns its index for later updates.
    public int AddLane(string label, Color color)
    {
        if (laneTemplate == null || lanesContainer == null) return -1;

        var lane = Instantiate(laneTemplate, lanesContainer);
        lane.gameObject.SetActive(true);
        lane.SetLabel(label);
        lane.SetColor(color);
        lane.SetSegment(0f, 0f); // empty until the Director colours its life window

        _lanes.Add(lane);
        BringOverlaysToFront(); // keep mask + playhead above the freshly added lane
        return _lanes.Count - 1;
    }

    // Colours lane's [start01, end01] window (its life span); the rest stays the grey track bg.
    public void SetLaneSegment(int lane, float start01, float end01)
    {
        if (lane >= 0 && lane < _lanes.Count) _lanes[lane].SetSegment(start01, end01);
    }

    // Moves the shared playhead and the future overlay. t01 is normalized over the timeline window
    // (0 = start, 1 = right edge). Drives only the horizontal anchor (mapped into [leftInset, width]
    // so it lines up with the lane bars); the height is layout-driven.
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

        if (_futureMask != null) // dim from the playhead to the right edge
        {
            _futureMask.anchorMin = new Vector2(x, 0f);
            _futureMask.anchorMax = new Vector2(1f, 1f);
            _futureMask.offsetMin = Vector2.zero;
            _futureMask.offsetMax = Vector2.zero;
        }
    }

    // Sets the runtime-resolved control legend shown inside the timeline (see hintLabel).
    public void SetHint(string text)
    {
        if (hintLabel != null) hintLabel.text = text;
    }

    // Show/hide the whole timeline via a CanvasGroup (keeps the GameObject active).
    public void SetVisible(bool visible)
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();

        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;
    }
}
