using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class Level : MonoBehaviour
{
    void Start()
    {
#if UNITY_EDITOR
        // Properly set the level index in the editor to avoid issues with the Play Mode Start Scene option
        if (LevelLoader.Index >= 0) return;

        var set = LevelLoader.Set;
        if (set == null) return;

        // Find the matching scene in the LevelSet
        var guid = AssetDatabase.AssetPathToGUID(gameObject.scene.path);
        int index = set.Scenes.FindIndex(s => s.AssetGUID == guid);

        // Update the index if found
        if (index >= 0)
            LevelLoader.SetIndex(index);
        else
            Debug.LogWarning($"Scene '{gameObject.scene.name}' was not found in the LevelSet.", this);
#endif
    }
}
