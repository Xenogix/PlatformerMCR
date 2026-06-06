using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One row of the ActionTimeline: a label + a fill bar that grows with recorded/played progress,
// plus optional event tick markers (jump, switch, death...). Instantiated from a template by
// ActionTimeline, so wiring is done once on the template.
//
// Expected hierarchy on the template:
//   LaneView (root, has this component + a HorizontalLayoutGroup or fixed layout)
//     ├─ Label   (TMP_Text)            -> label
//     └─ Track   (RectTransform)       -> track   (the bar background / event area)
//          └─ Fill (Image, stretch)    -> fill    (script drives its right edge, anchorMax.x)
//          └─ Tick (RectTransform)     -> tickTemplate (disabled marker, cloned per event)
public class LaneView : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private Image fill;
    [Tooltip("Area that ticks/progress map onto (usually the bar background, parent of Fill).")]
    [SerializeField] private RectTransform track;
    [Tooltip("Optional disabled marker cloned for each event. Leave empty to skip ticks.")]
    [SerializeField] private RectTransform tickTemplate;
    [Tooltip("Optional Button on the lane root, so the lane can be focused/selected (pause menu).")]
    [SerializeField] private Button button;

    // The lane's selectable, used by ActionTimeline for keyboard/gamepad navigation. Null if the
    // lane isn't meant to be selectable.
    public Button Button => button;

    public void SetLabel(string text)
    {
        if (label != null) label.text = text;
    }

    public void SetColor(Color color)
    {
        if (fill != null) fill.color = color;
    }

    // 0..1 portion of the lane that has been recorded/played. Drives the fill's right edge.
    public void SetProgress(float t01)
    {
        if (fill == null) return;
        var rt = fill.rectTransform;
        var max = rt.anchorMax;
        max.x = Mathf.Clamp01(t01);
        rt.anchorMax = max;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // Place a tick marker at normalized time t01 along the track.
    // NOTE: ticks must be parented to the full-width Track (not the Fill, which scales with
    // progress). The fields above must reflect that. A LayoutGroup on Track would override these
    // anchors and break the distribution.
    public void AddTick(float t01)
    {
        if (tickTemplate == null || track == null) return;

        // worldPositionStays:false keeps the template's local size/scale (UI-correct under a scaled canvas).
        var tick = Instantiate(tickTemplate, track, false);
        tick.gameObject.SetActive(true);

        // Pin to a vertical line at x, spanning the track height and centered on x. Width and height
        // come from the template; we only drive the horizontal anchor and re-center the pivot.
        float x = Mathf.Clamp01(t01);
        tick.anchorMin = new Vector2(x, 0f);
        tick.anchorMax = new Vector2(x, 1f);
        tick.pivot = new Vector2(0.5f, 0.5f);
        tick.anchoredPosition = Vector2.zero;
    }
}
