using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
