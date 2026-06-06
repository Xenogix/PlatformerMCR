using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One row of the ActionTimeline: a label + a coloured fill bar driven as a [start, end] slice
// (a clone's life window) over the lane's grey track background. Instantiated from a template by
// ActionTimeline, so wiring is done once on the template.
//
// Expected hierarchy on the template:
//   LaneView (root)
//     ├─ Label (TMP_Text)               -> label
//     └─ Track (grey background)
//          └─ Fill (Image, stretch)     -> fill   (script drives its horizontal anchors)
public class LaneView : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private Image fill;

    public void SetLabel(string text)
    {
        if (label != null) label.text = text;
    }

    public void SetColor(Color color)
    {
        if (fill != null) fill.color = color;
    }

    // Colours the [start01, end01] slice of the lane in the lane colour — its "life" window (a
    // clone's [spawn, end]) — leaving the rest as the grey track background. Drives both horizontal
    // edges of the fill.
    public void SetSegment(float start01, float end01)
    {
        if (fill == null) return;
        float a = Mathf.Clamp01(start01);
        float b = Mathf.Clamp01(end01);
        if (b < a) (a, b) = (b, a);
        var rt = fill.rectTransform;
        rt.anchorMin = new Vector2(a, rt.anchorMin.y);
        rt.anchorMax = new Vector2(b, rt.anchorMax.y);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
