using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

[CreateAssetMenu(menuName = "Levels/Level Set", fileName = "LevelSet")]
public class LevelSet : ScriptableObject
{
    public List<AssetReference> Scenes = new();
}