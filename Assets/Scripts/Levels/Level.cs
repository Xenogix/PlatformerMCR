using UnityEngine;

public class Level : MonoBehaviour
{
    [SerializeField] private LevelData data;
    [SerializeField] private LevelSet set;

    public LevelData Data => data;
    public LevelSet Set => set;

    public void Complete() => LevelLoader.LoadNext(set, data);

    public void Restart() => LevelLoader.Load(data);
}
