using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

public class LevelPainterSession : INotifyBindablePropertyChanged
{
    public event EventHandler<BindablePropertyChangedEventArgs> propertyChanged;

    [CreateProperty]
    public List<string> Palettes { get; set; } = new();

    private int selectedPaletteIndex = -1;
    [CreateProperty]
    public int SelectedPaletteIndex
    {
        get => selectedPaletteIndex;
        set { selectedPaletteIndex = value; Notify(); }
    }

    public GameObject SelectedPrefab { get; set; }

    private LevelPaintMode mode = LevelPaintMode.Paint;
    [CreateProperty]
    public LevelPaintMode Mode
    {
        get => mode;
        set { mode = value; Notify(); }
    }

    [CreateProperty]
    public ToggleButtonGroupState ModeState
    {
        get => ToggleButtonGroupState.FromEnumFlags(mode);
        set { mode = ToggleButtonGroupState.ToEnumFlags<LevelPaintMode>(value); Notify(); }
    }

    private int zLayer = 0;
    [CreateProperty]
    public int ZLayer
    {
        get => zLayer;
        set { zLayer = value; Notify(); }
    }

    private bool snapToGrid = true;
    [CreateProperty]
    public bool SnapToGrid
    {
        get => snapToGrid;
        set { snapToGrid = value; Notify(); }
    }

    private bool alignToNormal = false;
    [CreateProperty]
    public bool AlignToNormal
    {
        get => alignToNormal;
        set { alignToNormal = value; Notify(); }
    }

    private bool showGrid = true;
    [CreateProperty]
    public bool ShowGrid
    {
        get => showGrid;
        set { showGrid = value; Notify(); }
    }

    private List<LevelPainterPaletteDisplayItem> items = new();
    [CreateProperty]
    public List<LevelPainterPaletteDisplayItem> Items
    {
        get => items;
        set { items = value; Notify(); }
    }

    private int selectedItemIndex = -1;
    [CreateProperty]
    public int SelectedItemIndex
    {
        get => selectedItemIndex;
        set { selectedItemIndex = value; Notify(); }
    }

    private string searchFilter = string.Empty;
    [CreateProperty]
    public string SearchFilter
    {
        get => searchFilter;
        set { searchFilter = value; Notify(); }
    }

    private int rotation = 0;
    [CreateProperty]
    public int Rotation
    {
        get => rotation;
        set { rotation = ((value % 360) + 360) % 360; Notify(); }
    }

    /// <summary>Cycles rotation by 90° on each press: 0 → 90 → 180 → 270 → 0.</summary>
    public void CycleRotation()
    {
        Rotation = (rotation + 90) % 360;
    }

    private bool flipY = false;
    [CreateProperty]
    public bool FlipY
    {
        get => flipY;
        set { flipY = value; Notify(); }
    }

    /// <summary>Toggles a 180° flip around the Y axis (mirrors the object left/right).</summary>
    public void ToggleFlipY() => FlipY = !flipY;

    // Resets the search filter backing field without firing propertyChanged
    public void ResetSearchFilter() => searchFilter = string.Empty;

    private void Notify([CallerMemberName] string property = "") =>
        propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(property));
}
