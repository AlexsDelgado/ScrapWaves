using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum MaterialDisplayLayout
{
    Vertical,
    Horizontal
}

/// <summary>
/// Muestra el inventario de materiales con icono, nombre y cantidad.
/// Reutilizable en HUD y pantallas de crafteo.
/// </summary>
[DisallowMultipleComponent]
public class MaterialInventoryDisplayView : MonoBehaviour
{
    private struct Entry
    {
        public MaterialType Type;
        public GameObject Root;
        public TextMeshProUGUI AmountLabel;
    }

    [SerializeField] private MaterialDisplayLayout _layout = MaterialDisplayLayout.Vertical;
    [SerializeField, Min(12f)] private float _iconSize = 22f;
    [SerializeField, Min(8f)] private float _fontSize = 16f;
    [SerializeField, Min(0f)] private float _spacing = 6f;
    [SerializeField] private bool _showEmpty = true;
    [SerializeField] private bool _showNames;

    private readonly List<Entry> _entries = new();
    private RectTransform _contentRoot;

    public static MaterialInventoryDisplayView Create(
        Transform parent,
        MaterialDisplayLayout layout,
        bool showEmpty = true,
        bool showNames = true,
        float iconSize = 22f,
        float fontSize = 16f,
        float spacing = 6f)
    {
        var go = new GameObject("MaterialInventoryDisplay", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var view = go.AddComponent<MaterialInventoryDisplayView>();
        view._layout = layout;
        view._showEmpty = showEmpty;
        view._showNames = showNames;
        view._iconSize = iconSize;
        view._fontSize = fontSize;
        view._spacing = spacing;
        view.EnsureEntries();
        return view;
    }

    public void Configure(MaterialDisplayLayout layout, bool showEmpty, bool showNames)
    {
        _layout = layout;
        _showEmpty = showEmpty;
        _showNames = showNames;
        EnsureEntries();
    }

    public void Refresh(MaterialInventory inventory)
    {
        EnsureEntries();

        foreach (Entry entry in _entries)
        {
            int amount = inventory != null ? inventory.GetAmount(entry.Type) : 0;
            bool visible = _showEmpty || amount > 0;
            entry.Root.SetActive(visible);
            if (!visible)
                continue;

            entry.AmountLabel.text = amount.ToString();
        }

        if (_contentRoot != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRoot);
    }

    private void EnsureEntries()
    {
        if (_contentRoot != null && _entries.Count > 0)
            return;

        ClearEntries();

        _contentRoot = GetComponent<RectTransform>();
        _contentRoot.anchorMin = new Vector2(0f, 1f);
        _contentRoot.anchorMax = new Vector2(0f, 1f);
        _contentRoot.pivot = new Vector2(0f, 1f);
        _contentRoot.anchoredPosition = Vector2.zero;

        var layout = gameObject.AddComponent<LayoutGroupForMaterialDisplay>();
        layout.Configure(_layout, _spacing);

        foreach (MaterialType type in Enum.GetValues(typeof(MaterialType)))
            _entries.Add(CreateEntry(type));

        LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRoot);
    }

    private Entry CreateEntry(MaterialType type)
    {
        var rowGo = new GameObject(MaterialCatalog.GetDisplayName(type), typeof(RectTransform));
        rowGo.transform.SetParent(_contentRoot, false);

        var rowLayout = rowGo.AddComponent<HorizontalLayoutGroup>();
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.spacing = 4f;
        rowLayout.childControlWidth = false;
        rowLayout.childControlHeight = false;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;

        var fitter = rowGo.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Image icon = HudUiFactory.CreateIconSlot(
            rowGo.transform,
            "Icon",
            _iconSize,
            null,
            HudPlaceholderKind.None);
        icon.color = MaterialCatalog.GetUiColor(type);

        if (_showNames)
        {
            TextMeshProUGUI nameLabel = CreateAmountLabel(rowGo.transform, "Name");
            nameLabel.fontSize = _fontSize;
            nameLabel.fontStyle = FontStyles.Normal;
            nameLabel.color = HudUiFactory.MutedTextColor;
            nameLabel.text = MaterialCatalog.GetDisplayName(type);
            var nameLayout = nameLabel.gameObject.AddComponent<LayoutElement>();
            nameLayout.minWidth = 112f;
        }

        TextMeshProUGUI amountLabel = CreateAmountLabel(rowGo.transform, "Amount");
        amountLabel.fontSize = _fontSize;
        amountLabel.fontStyle = FontStyles.Bold;
        amountLabel.text = "0";
        amountLabel.color = Color.white;

        var amountLayout = amountLabel.gameObject.AddComponent<LayoutElement>();
        amountLayout.minWidth = 32f;

        return new Entry
        {
            Type = type,
            Root = rowGo,
            AmountLabel = amountLabel
        };
    }

    private static TextMeshProUGUI CreateAmountLabel(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var label = go.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(label);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.color = Color.white;
        label.raycastTarget = false;
        return label;
    }

    private void ClearEntries()
    {
        _entries.Clear();
        if (_contentRoot == null)
            _contentRoot = GetComponent<RectTransform>();

        for (int i = _contentRoot.childCount - 1; i >= 0; i--)
            Destroy(_contentRoot.GetChild(i).gameObject);

        LayoutGroup existingLayout = GetComponent<LayoutGroup>();
        if (existingLayout != null)
            Destroy(existingLayout);

        LayoutGroupForMaterialDisplay helper = GetComponent<LayoutGroupForMaterialDisplay>();
        if (helper != null)
            Destroy(helper);
    }

    private sealed class LayoutGroupForMaterialDisplay : MonoBehaviour
    {
        private LayoutGroup _layoutGroup;

        public void Configure(MaterialDisplayLayout layout, float spacing)
        {
            if (_layoutGroup != null)
                Destroy(_layoutGroup);

            if (layout == MaterialDisplayLayout.Horizontal)
            {
                var horizontal = gameObject.AddComponent<HorizontalLayoutGroup>();
                horizontal.childAlignment = TextAnchor.MiddleCenter;
                horizontal.spacing = spacing;
                horizontal.childControlWidth = false;
                horizontal.childControlHeight = false;
                horizontal.childForceExpandWidth = false;
                horizontal.childForceExpandHeight = false;
                _layoutGroup = horizontal;
            }
            else
            {
                var vertical = gameObject.AddComponent<VerticalLayoutGroup>();
                vertical.childAlignment = TextAnchor.UpperLeft;
                vertical.spacing = spacing;
                vertical.childControlWidth = false;
                vertical.childControlHeight = false;
                vertical.childForceExpandWidth = false;
                vertical.childForceExpandHeight = false;
                _layoutGroup = vertical;
            }

            var fitter = gameObject.GetComponent<ContentSizeFitter>();
            if (fitter == null)
                fitter = gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }
    }
}
