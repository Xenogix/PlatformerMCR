using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class LevelPainterWindow : EditorWindow
{
    [SerializeField]
    private VisualTreeAsset m_VisualTreeAsset = default;

    private IReadOnlyList<LevelPalette> palettes;
    private LevelPalette currentPalette;
    private ListView itemsListView;

    private LevelPainterSession session { get; } = new();

    public static LevelPainterSession ActiveSession { get; private set; }

    [MenuItem("Window/Level Painter")]
    public static void ShowWindow()
    {
        LevelPainterWindow wnd = GetWindow<LevelPainterWindow>();
        wnd.titleContent = new GUIContent("Level Painter");
    }

    public void CreateGUI()
    {
        ActiveSession = session;

        VisualElement root = rootVisualElement;
        VisualElement content = m_VisualTreeAsset.Instantiate();
        root.Add(content);
        content.dataSource = session;

        session.propertyChanged += (sender, args) =>
        {
            if (args.propertyName == nameof(LevelPainterSession.SelectedPaletteIndex))
                LoadPalette(palettes.ElementAtOrDefault(session.SelectedPaletteIndex));
            else if (args.propertyName == nameof(LevelPainterSession.SelectedItemIndex))
                session.SelectedPrefab = session.Items.ElementAtOrDefault(session.SelectedItemIndex)?.Prefab;
            else if (args.propertyName == nameof(LevelPainterSession.SearchFilter))
                ApplyFilter();
        };

        RefreshPalettes();

        itemsListView = content.Q<ListView>("Items");
        if (itemsListView != null)
        {
            itemsListView.RegisterCallback<DragUpdatedEvent>(OnDragUpdated, TrickleDown.TrickleDown);
            itemsListView.RegisterCallback<DragPerformEvent>(OnDragPerform, TrickleDown.TrickleDown);
        }

        }

        public void OnDestroy()
    {
        EditorApplication.update -= PollAssetPreviews;
        if (ActiveSession == session)
            ActiveSession = null;
    }

    private void ApplyFilter()
    {
        var filter = NormalizeName(session.SearchFilter ?? string.Empty);

        var source = currentPalette?.Items.Where(i => i?.Prefab != null) ?? Enumerable.Empty<LevelPaletteItem>();

        session.Items = source
            .Where(i => string.IsNullOrWhiteSpace(filter) || NormalizeName(i.Prefab.name).Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.Prefab.name)
            .Select(i => new LevelPainterPaletteDisplayItem
            {
                Name = NormalizeName(i.Prefab.name),
                Prefab = i.Prefab,
                Texture = AssetPreview.GetAssetPreview(i.Prefab)
            })
            .ToList();

        session.SelectedItemIndex = -1;

        AssetPreview.SetPreviewTextureCacheSize(session.Items.Count + 10);

        EditorApplication.update -= PollAssetPreviews;
        EditorApplication.update += PollAssetPreviews;
    }

    private void PollAssetPreviews()
    {
        bool anyPending = false;

        foreach (var item in session.Items)
        {
            if (item.Texture != null) continue;

            item.Texture = AssetPreview.GetAssetPreview(item.Prefab);

            if (item.Texture == null)
                anyPending = true;
        }

        if (!anyPending)
            EditorApplication.update -= PollAssetPreviews;
    }

    private void OnDragUpdated(DragUpdatedEvent evt)
    {
        DragAndDrop.visualMode = DragAndDrop.objectReferences.OfType<GameObject>().Any()
            ? DragAndDropVisualMode.Copy
            : DragAndDropVisualMode.Rejected;
        evt.StopPropagation();
    }

    private void OnDragPerform(DragPerformEvent evt)
    {
        if (currentPalette == null) return;

        var dropped = DragAndDrop.objectReferences.OfType<GameObject>().ToList();
        if (dropped.Count == 0) return;

        DragAndDrop.AcceptDrag();

        var serializedPalette = new SerializedObject(currentPalette);
        var itemsList = serializedPalette.FindProperty("items");

        foreach (var go in dropped)
        {
            itemsList.arraySize++;
            var newItem = itemsList.GetArrayElementAtIndex(itemsList.arraySize - 1);
            newItem.FindPropertyRelative("prefab").objectReferenceValue = go;
        }

        serializedPalette.ApplyModifiedProperties();

        AssetDatabase.SaveAssets();

        LoadPalette(currentPalette);
        evt.StopPropagation();
    }

    private void RefreshPalettes()
    {
        var selected = currentPalette;
        palettes = LevelPaletteService.GetPalettes();
        session.Palettes = palettes?.Select(p => p.PaletteName).ToList() ?? new List<string>();

        var restoredIndex = palettes?.Select((p, i) => (p, i)).FirstOrDefault(x => x.p == selected).i ?? 0;
        session.SelectedPaletteIndex = palettes != null && palettes.Count > 0 ? restoredIndex : -1;
    }

    private void LoadPalette(LevelPalette palette)
    {
        currentPalette = palette;
        session.ResetSearchFilter();
        ApplyFilter();
    }

    private string NormalizeName(string name) => name.Replace("_", " ");
}
