using System;
using UnityEngine;

[Serializable]
public class LevelPaletteItem
{
    [SerializeField]
    private GameObject prefab;

    public GameObject Prefab => prefab;
}
