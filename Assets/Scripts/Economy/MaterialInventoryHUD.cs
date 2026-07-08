using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD pequeña que muestra la cantidad de cada material del inventario
/// (formato "Nombre: cantidad") para consulta rápida durante el crafteo.
/// Construye su propio canvas en runtime y se refresca por evento.
/// </summary>
[DisallowMultipleComponent]
public class MaterialInventoryHUD : MonoBehaviour
{
    [SerializeField] private MaterialInventory _inventory;
    [SerializeField, Tooltip("Posición anclada respecto de la esquina superior izquierda.")]
    private Vector2 _anchoredPosition = new Vector2(16f, -16f);
    [SerializeField, Min(8f)] private float _fontSize = 18f;
    [SerializeField, Tooltip("Si está activo, muestra todos los materiales; si no, solo los que tengas (>0).")]
    private bool _showEmpty = true;

    private TextMeshProUGUI _text;

    private void Awake() => ResolveInventory();

    private void OnEnable()
    {
        ResolveInventory();
        EnsureUi();
        if (_inventory != null)
        {
            _inventory.OnInventoryChanged += Refresh;
            Refresh();
        }
    }

    private void OnDisable()
    {
        if (_inventory != null)
            _inventory.OnInventoryChanged -= Refresh;
    }

    private void ResolveInventory()
    {
        if (_inventory != null)
            return;
        _inventory = MaterialInventory.Instance != null
            ? MaterialInventory.Instance
            : FindAnyObjectByType<MaterialInventory>();
    }

    private void Refresh()
    {
        if (_text == null)
            return;

        var sb = new StringBuilder();
        foreach (MaterialType type in Enum.GetValues(typeof(MaterialType)))
        {
            int amount = _inventory != null ? _inventory.GetAmount(type) : 0;
            if (!_showEmpty && amount <= 0)
                continue;

            if (sb.Length > 0)
                sb.Append('\n');
            sb.Append(MaterialCatalog.GetDisplayName(type)).Append(": ").Append(amount);
        }

        _text.text = sb.ToString();
    }

    private void EnsureUi()
    {
        if (_text != null)
            return;

        var canvasGo = new GameObject("MaterialInventoryHUDCanvas", typeof(RectTransform));
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(canvasGo.transform, false);
        var panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0f, 1f);
        panelRt.anchorMax = new Vector2(0f, 1f);
        panelRt.pivot = new Vector2(0f, 1f);
        panelRt.anchoredPosition = _anchoredPosition;
        panelRt.sizeDelta = new Vector2(220f, 180f);
        var panelImg = panel.AddComponent<Image>();
        panelImg.sprite = HudUiFactory.WhiteSprite;
        panelImg.color = new Color(0f, 0f, 0f, 0.45f);
        panelImg.raycastTarget = false;

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(panel.transform, false);
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(10f, 8f);
        textRt.offsetMax = new Vector2(-10f, -8f);
        _text = textGo.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(_text);
        _text.fontSize = _fontSize;
        _text.alignment = TextAlignmentOptions.TopLeft;
        _text.color = Color.white;
        _text.raycastTarget = false;
    }
}
