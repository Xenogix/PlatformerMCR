using UnityEngine;
using UnityEngine.AddressableAssets;

[CreateAssetMenu(menuName = "Levels/Level Data", fileName = "LevelData")]
public class LevelData : ScriptableObject
{
    public string DisplayName;
    public AssetReference SceneRef;
}
