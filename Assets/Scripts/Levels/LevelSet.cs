using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Levels/Level Set", fileName = "LevelSet")]
public class LevelSet : ScriptableObject
{
    public List<LevelData> Levels = new();

    public int Count => Levels.Count;

    /// <summary>Returns the level at <paramref name="index"/>, or null if out of range.</summary>
    public LevelData Get(int index) => (index >= 0 && index < Levels.Count) ? Levels[index] : null;

    public int IndexOf(LevelData data) => Levels.IndexOf(data);
}
