using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Data", menuName = "ScriptableObjects/LevelPainterPalette")]
public class LevelPalette : ScriptableObject
{
    [SerializeField]
    private List<LevelPaletteItem> items = new();

    public string PaletteName => name;

    public IEnumerable<LevelPaletteItem> Items => items;
}
