using System.Text.RegularExpressions;
using UnityEngine;

public class GridObject : MonoBehaviour
{
    public enum Pivot { Center, Start, End }

    [Min(1)]
    public int SizeX = 1;

    [Min(1)]
    public int SizeY = 1;

    [Min(1)]
    public int SizeZ = 1;

    public Pivot PivotX = Pivot.Center;
    public Pivot PivotY = Pivot.Center;

    private static readonly Regex s_SizePattern = new(@"(\d+)x(\d+)x(\d+)", RegexOptions.IgnoreCase);
    private void Reset() => TryParseNameDimensions();
    public void TryParseNameDimensions()
    {
        var m = s_SizePattern.Match(gameObject.name);
        if (!m.Success) return;
        SizeX = Mathf.Max(1, int.Parse(m.Groups[1].Value));
        SizeY = Mathf.Max(1, int.Parse(m.Groups[3].Value));
        SizeZ = Mathf.Max(1, int.Parse(m.Groups[2].Value));
    }
}
