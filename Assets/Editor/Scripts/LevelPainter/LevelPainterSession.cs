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

    private bool flipX = false;
    [CreateProperty]
    public bool FlipX
    {
        get => flipX;
        set { flipX = value; Notify(); }
    }

    private bool flipY = false;
    [CreateProperty]
    public bool FlipY
    {
        get => flipY;
        set { flipY = value; Notify(); }
    }

    /// <summary>
    /// Cycles through the four flip orientations in order:
    /// normal → flip X → flip X+Y → flip Y → normal.
    /// </summary>
    public void CycleFlip()
    {
        (flipX, flipY) = (flipX, flipY) switch
        {
            (false, false) => (true,  false),
            (true,  false) => (true,  true ),
            (true,  true ) => (false, true ),
            _              => (false, false),
        };
        Notify(nameof(FlipX));
        Notify(nameof(FlipY));
    }

    // Resets the search filter backing field without firing propertyChanged
    public void ResetSearchFilter() => searchFilter = string.Empty;

    private void Notify([CallerMemberName] string property = "") =>
        propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(property));
}
