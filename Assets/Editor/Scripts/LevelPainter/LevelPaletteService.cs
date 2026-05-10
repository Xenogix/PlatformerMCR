using System.Collections.Generic;
using System.Linq;
using UnityEditor;

public static class LevelPaletteService
{
    public static IReadOnlyList<LevelPalette> GetPalettes()
    {
        return AssetDatabase.FindAssets($"t:{nameof(LevelPalette)}")
            .Select(guid => AssetDatabase.LoadAssetAtPath<LevelPalette>(AssetDatabase.GUIDToAssetPath(guid)))
            .ToList();
    }

    public static LevelPalette GetPalette(int index)
    {
        var list = GetPalettes();
        if (index < 0 || index >= list.Count) return null;
        return list[index];
    }
}
