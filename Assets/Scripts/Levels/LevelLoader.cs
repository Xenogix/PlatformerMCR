using System.Linq;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

public static class LevelLoader
{
    private const string LevelSetAddress = "levelSet";

    private static LevelSet _set;
    public static LevelSet Set
    {
        get
        {
            if (_set == null)
            {
                var handle = Addressables.LoadAssetAsync<LevelSet>(LevelSetAddress);
                _set = handle.WaitForCompletion();
            }
            return _set;
        }
    }

    public static int Index { get; private set; } = -1;

    public static AsyncOperationHandle<SceneInstance> Load(int index)
    {
        var scene = Set.Scenes.ElementAtOrDefault(index);
        if (scene == null) return default;

        Index = index;
        return scene.LoadSceneAsync(LoadSceneMode.Single);
    }

    // Loop the roster: after the last level, wrap back to the first (…→09→01).
    public static void LoadNext()
    {
        int count = Set != null ? Set.Scenes.Count : 0;
        Load(count > 0 ? (Index + 1) % count : Index + 1);
    }
    public static void Restart() => Load(Index);
    public static void SetIndex(int index) => Index = index;
}
