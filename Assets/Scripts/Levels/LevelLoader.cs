using UnityEngine.SceneManagement;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;

/// <summary>
/// Loads level scenes in Single mode via Addressables. Each level scene is self-contained (it brings its
/// own Main Camera + CinemachineBrain), so a Single load cleanly swaps everything — no persistent objects,
/// no cross-scene camera. Order lives in the <see cref="LevelSet"/>.
/// </summary>
public static class LevelLoader
{
    /// <summary>Load a specific level (also used by a level-select menu later).</summary>
    public static AsyncOperationHandle<SceneInstance> Load(LevelData level)
        => level.SceneRef.LoadSceneAsync(LoadSceneMode.Single);

    /// <summary>Load the level after <paramref name="current"/> in <paramref name="set"/>.</summary>
    public static void LoadNext(LevelSet set, LevelData current)
    {
        if (set == null || current == null) return;
        var next = set.Get(set.IndexOf(current) + 1);
        if (next == null) return;
        Load(next);
    }
}
