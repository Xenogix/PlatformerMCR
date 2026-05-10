using System;
using System.Runtime.CompilerServices;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

[Serializable]
public class LevelPainterPaletteDisplayItem : INotifyBindablePropertyChanged
{
    public event EventHandler<BindablePropertyChangedEventArgs> propertyChanged;

    private Texture2D texture;
    [CreateProperty]
    public Texture2D Texture
    {
        get => texture;
        set { texture = value; Notify(); }
    }

    [CreateProperty]
    public string Name { get; set; }

    public GameObject Prefab { get; set; }

    private void Notify([CallerMemberName] string property = "") =>
        propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(property));
}
